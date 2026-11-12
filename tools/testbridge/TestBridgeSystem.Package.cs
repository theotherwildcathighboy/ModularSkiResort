// Op "package": the Editor's package step for the release .cok, without the hand selection (README.md, "Packaging the release .cok").
// It resolves tools/shipped-prefabs.json to the user database's PrefabAssets and fills the game's own DependencyCollector with them,
// as the Editor's package panel does when you add assets (Game.UI.Editor/PackagePanelSystem.cs:331-339, decompile 1.6.4f1). The real
// mode then calls the same PackageHelper.CreatePackage as the panel's Create Package button (PackagePanelSystem.cs:216-222): compile
// the closure (DependencyCollector.cs:267-276), copy it to a staging database, resave the prefabs, convert the surfaces to virtual
// texturing and add the package to AssetDatabase.packages (PackageHelper.cs:73-121), which writes
// %USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\.packages\<main asset>.cok.
// It NEVER uploads: no PdxAssetUploadHandle, no Paradox Mods call. The upload stays the owner's `dotnet publish` of the hybrid item.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Colossal.IO.AssetDatabase;
using Game.SceneFlow;
using Game.UI.Editor;
using Newtonsoft.Json.Linq;

namespace SkiTestBridge
{
    public partial class TestBridgeSystem
    {
        private sealed class PackageJob
        {
            public string main;
            public string name;
            public DateTime started;
            public DateTime? finished;
            public string message;
            public float progress;
            public bool? success;
            public string path;
            public long bytes;
            public string error;
            public int closure;
        }

        private PackageJob m_PackageJob;

        // args: list (absolute path of shipped-prefabs.json, required), dry (default true), main (the main asset; default the first entry of
        // the list), name (the package and .cok name; default the main asset's name, as the Editor does), status (only the state of the last real run), show (rows per
        // detail list, default 40).
        private JObject PackageCommand(JObject args)
        {
            if ((bool?)args["status"] ?? false)
                return PackageStatus();
            if (m_PackageJob != null && m_PackageJob.finished == null)
                throw new InvalidOperationException("a package build is running: {op:\"package\",status:true}");

            string listPath = (string)args["list"];
            if (string.IsNullOrEmpty(listPath) || !Path.IsPathRooted(listPath) || !File.Exists(listPath))
                throw new ArgumentException("list: the absolute path of tools/shipped-prefabs.json");
            bool dry = (bool?)args["dry"] ?? true;
            string packageName = (string)args["name"];
            if (packageName != null && (packageName.Length == 0 || packageName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
                throw new ArgumentException("name: a bare file name");
            int show = (int?)args["show"] ?? 40;
            var shipped = (JArray)JObject.Parse(File.ReadAllText(listPath))["prefabs"];

            // Index the user database (ImportedData) once: by content id and by name. The Editor's panel offers exactly this database
            // (PackagePanelSystem.cs:416).
            var userPrefabs = AssetDatabase.user.GetAssets(default(SearchFilter<PrefabAsset>)).ToList();
            var byGuid = new Dictionary<Colossal.Hash128, PrefabAsset>();
            var byName = new Dictionary<string, List<PrefabAsset>>(StringComparer.OrdinalIgnoreCase);
            foreach (PrefabAsset a in userPrefabs)
            {
                byGuid[a.id.guid] = a;
                if (!byName.TryGetValue(a.name, out var list))
                    byName[a.name] = list = new List<PrefabAsset>();
                list.Add(a);
            }

            var roots = new List<AssetData>();
            var missing = new JArray();
            var nameOnly = new JArray();
            foreach (JObject row in shipped)
            {
                string name = (string)row["name"];
                string cid = (string)row["cid"];
                if (!string.IsNullOrEmpty(cid) && Colossal.Hash128.TryParse(cid, out Colossal.Hash128 guid) && byGuid.TryGetValue(guid, out PrefabAsset hit))
                {
                    roots.Add(hit);
                    continue;
                }
                // A content id that does not resolve would make check-cok report CID; list it so the dry run shows it first.
                string stem = Path.GetFileNameWithoutExtension((string)row["file"] ?? name);
                if ((byName.TryGetValue(name, out var named) || byName.TryGetValue(stem, out named)) && named.Count == 1)
                {
                    nameOnly.Add(new JObject { ["name"] = name, ["frozenCid"] = cid, ["dbCid"] = named[0].id.guid.ToString() });
                    roots.Add(named[0]);
                    continue;
                }
                missing.Add(name);
            }

            AssetData main = roots.FirstOrDefault();
            string mainName = (string)args["main"];
            if (!string.IsNullOrEmpty(mainName))
            {
                main = roots.FirstOrDefault(r => string.Equals(r.name, mainName, StringComparison.OrdinalIgnoreCase));
                if (main == null)
                    throw new ArgumentException($"main '{mainName}' is not one of the resolved shipped prefabs");
            }

            // The collector's first asset becomes the main asset (DependencyCollector.cs:146-149).
            var collector = new DependencyCollector();
            if (main != null)
            {
                collector.AddAssets(new[] { main }.Concat(roots.Where(r => r != main)));
            }
            AssetData[] closure = collector.CompileAllAssets();

            var shippedIds = new HashSet<AssetData>(roots);
            var extraPrefabs = closure.OfType<PrefabAsset>().Where(a => !shippedIds.Contains(a)).Select(a => a.name).OrderBy(n => n).ToList();
            var byType = new JObject();
            var byDatabase = new JObject();
            long totalBytes = 0;
            var biggest = new List<(string, long)>();
            foreach (AssetData a in closure)
            {
                string type = a.GetType().Name;
                byType[type] = ((int?)byType[type] ?? 0) + 1;
                string db = a.database == AssetDatabase.user ? "user" : (a.database?.ToString() ?? "?");
                byDatabase[db] = ((int?)byDatabase[db] ?? 0) + 1;
                long size = 0;
                try { if (!string.IsNullOrEmpty(a.path) && File.Exists(a.path)) size = new FileInfo(a.path).Length; }
                catch (Exception) { }
                totalBytes += size;
                biggest.Add((a.name + " (" + type + ")", size));
            }
            var unselected = userPrefabs.Where(a => !shippedIds.Contains(a) && !collector.Contains(a)).Select(a => a.name).OrderBy(n => n).ToList();

            var result = new JObject
            {
                ["mode"] = dry ? "dry" : "real",
                ["gameMode"] = GameManager.instance.gameMode.ToString(),
                ["shipped"] = shipped.Count,
                ["found"] = roots.Count,
                ["missing"] = missing,
                ["cidMismatch"] = nameOnly,
                ["main"] = main?.name,
                ["packageName"] = packageName ?? main?.name,
                ["closure"] = new JObject
                {
                    ["assets"] = closure.Length,
                    ["bytes"] = totalBytes,
                    ["mb"] = Math.Round(totalBytes / 1048576.0, 1),
                    ["byType"] = byType,
                    ["byDatabase"] = byDatabase,
                    ["biggest"] = new JArray(biggest.OrderByDescending(b => b.Item2).Take(10).Select(b => b.Item1 + " " + Math.Round(b.Item2 / 1048576.0, 1) + " MB")),
                },
                ["extraPrefabsInClosure"] = new JArray(extraPrefabs.Take(show)),
                ["extraPrefabsInClosureCount"] = extraPrefabs.Count,
                ["modDependencies"] = new JArray(collector.modDependencies),
                ["dlcDependencies"] = new JArray(collector.dlcDependencies.Select(d => d.ToString())),
                ["userPrefabs"] = userPrefabs.Count,
                ["userPrefabsLeftOut"] = unselected.Count,
                ["userPrefabsLeftOutSample"] = new JArray(unselected.Take(show)),
            };
            if (dry)
                return result;

            if (missing.Count > 0 || nameOnly.Count > 0)
                throw new InvalidOperationException($"refusing a real run: {missing.Count} missing, {nameOnly.Count} content-id mismatches (run dry first)");
            if (collector.modDependencies.Count > 0 || collector.dlcDependencies.Count > 0)
                throw new InvalidOperationException("refusing a real run: the closure has mod or DLC dependencies (see the dry run)");
            if (GameManager.instance.gameMode != global::Game.GameMode.Editor)
                throw new InvalidOperationException($"the real run needs the Editor (game mode is {GameManager.instance.gameMode}): start with --startEditor");

            var job = new PackageJob { main = main.name, started = DateTime.Now, closure = closure.Length };
            m_PackageJob = job;
            Mod.Log.Info($"package: building '{packageName ?? main.name}' (main asset {main.name}) from {roots.Count} shipped prefabs, closure {closure.Length} assets, {totalBytes / 1048576} MB");
            // The panel's button calls OnCreatePackage, which awaits this same task (PackageHelper.cs:20-23). Its continuations come back to
            // the main thread, so the bridge stays responsive only between steps: the resave and the VT conversion block frames.
            Action<string, float> onProgress = (message, progress) =>
            {
                if (!string.IsNullOrEmpty(message))
                    job.message = message;
                job.progress = progress;
            };
            Task<PackageAsset> build;
            if (string.IsNullOrEmpty(packageName))
            {
                build = PackageHelper.CreatePackage(collector, onProgress);
            }
            else
            {
                // CreatePackage names the package after the main asset (PackageHelper.cs:38) and hands the name to the private
                // BuildPackage(packageName, mainAsset, assets, dependencies, progress) (:73), which uses it for the staging folder, the backup
                // folder and AssetDatabase.packages.AddAsset (:80, :83, :109) only. So this replays CreatePackage's lines 29-41 with our name:
                // same closure, same dependency data, same build. No file is renamed afterward.
                AssetData[] assets = collector.CompileAllAssets();
                var dependencies = new PackageDependencies.Data
                {
                    modDependencies = collector.modDependencies.ToArray(),
                    dlcDependencies = new string[0], // refused above when not empty
                    previews = collector.previews.Keys.ToArray(),
                };
                MethodInfo method = typeof(PackageHelper).GetMethod("BuildPackage", BindingFlags.NonPublic | BindingFlags.Static)
                    ?? throw new MissingMethodException("PackageHelper.BuildPackage is gone (game patch?): run without name");
                build = (Task<PackageAsset>)method.Invoke(null, new object[] { packageName, collector.mainAsset, assets, dependencies, onProgress });
            }
            job.name = packageName ?? main.name;
            build.ContinueWith(t =>
            {
                PackageAsset package = t.Status == TaskStatus.RanToCompletion ? t.Result : null;
                job.success = package != null;
                if (package != null)
                {
                    job.path = package.path;
                    try { job.bytes = new FileInfo(package.path).Length; } catch (Exception) { }
                }
                else
                {
                    job.error = t.Exception?.GetBaseException().Message ?? "the package build returned nothing: see Logs/Editor.log";
                }
                job.finished = DateTime.Now;
                Mod.Log.Info($"package: done, success {job.success}, {job.path}, {job.bytes} bytes, {job.error}");
            });
            result["started"] = true;
            result["poll"] = "{op:\"package\",status:true}";
            return result;
        }

        private JObject PackageStatus()
        {
            var job = m_PackageJob;
            if (job == null)
                return new JObject { ["state"] = "none" };
            return new JObject
            {
                ["state"] = job.finished == null ? "running" : (job.success == true ? "done" : "failed"),
                ["main"] = job.main,
                ["name"] = job.name,
                ["closure"] = job.closure,
                ["message"] = job.message,
                ["progress"] = Math.Round(job.progress, 3),
                ["seconds"] = Math.Round(((job.finished ?? DateTime.Now) - job.started).TotalSeconds, 1),
                ["path"] = job.path,
                ["bytes"] = job.bytes,
                ["mb"] = Math.Round(job.bytes / 1048576.0, 1),
                ["error"] = job.error,
            };
        }
    }
}
