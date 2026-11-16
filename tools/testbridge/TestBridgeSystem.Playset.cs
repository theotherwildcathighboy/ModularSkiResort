// T221 (round 2): playsets by the game's own Paradox SDK, so tools/world.ps1 can switch playsets without the owner clicking.
//
// The sanctioned path: PlatformManager -> PdxSdkPlatform (Colossal.PSI.PdxSdk) -> its IContext (private field m_SDKContext) -> Mods
// (PDX.SDK IModsService : IPlaysetsService). The launcher and the in-game Paradox Mods screen use the same calls; the SDK writes
// playset_config.json itself. Nothing here ever edits that file (decompile 1.6.4f1-pdxsdk: IPlaysetsService, PdxSdkPlatform.cs:1750).
//
// Safety rules, enforced here (T221: agents may create playsets named "SRM Test ...", reuse before create, never delete or edit the
// owner's playsets):
//   list / active / status   read only (ListAllPlaysets is called with includeOnline false: no network).
//   activate                 switches the ACTIVE playset (any name): the one change world.ps1 needs. Restart the game afterwards: a
//                            code mod's assembly loads once per run (the game re-reads the mod list at once, but treat the run as tainted).
//   create / clone           only for a name that starts with "SRM Test ". An existing playset of that name is reused, nothing is created.
//   enable                   enables or disables ONE mod (modId, enable) in the ACTIVE playset, only when its name starts with "SRM Test ".
//   There is no delete, no rename and no update.
//
// The calls are async: an op starts the job and returns {state:"running"}; {op:"playset",action:"status"} returns the job's result.
// bridge-call.mjs users: tools/playset.ps1 wraps both calls.
//
//   {op:"playset", action:"list"}
//   {op:"playset", action:"active"}
//   {op:"playset", action:"activate", name:"Standard Mod Set"}          (or id:"...")
//   {op:"playset", action:"clone", from:"Standard Mod Set", name:"SRM Test Asset"}
//   {op:"playset", action:"create", name:"SRM Test Empty"}
//   {op:"playset", action:"enable", modId:"167215", enable:false}
//   {op:"playset", action:"status"}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Colossal.PSI.Common;
using Colossal.PSI.PdxSdk;
using Newtonsoft.Json.Linq;
using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Results;
using PDX.SDK.Contracts.Service.Mods.Models;
using PDX.SDK.Contracts.Service.Mods.Results;

namespace SkiTestBridge
{
    public partial class TestBridgeSystem
    {
        private const string kTestPlaysetPrefix = "SRM Test ";

        private sealed class PlaysetJob
        {
            public string action;
            public string state = "running";   // running | done | failed
            public JObject result;
            public string error;
            public double startedAt;
        }

        private PlaysetJob m_PlaysetJob;

        private JObject PlaysetCommand(JObject args)
        {
            string action = (string)args["action"] ?? "status";
            if (action == "status")
            {
                if (m_PlaysetJob == null)
                    return new JObject { ["state"] = "idle" };
                var s = new JObject { ["state"] = m_PlaysetJob.state, ["action"] = m_PlaysetJob.action, ["elapsedS"] = Math.Round(Now - m_PlaysetJob.startedAt, 1) };
                if (m_PlaysetJob.result != null) s["result"] = m_PlaysetJob.result;
                if (m_PlaysetJob.error != null) s["error"] = m_PlaysetJob.error;
                return s;
            }
            if (m_PlaysetJob != null && m_PlaysetJob.state == "running" && Now - m_PlaysetJob.startedAt < 120)
                throw new InvalidOperationException($"a playset job ({m_PlaysetJob.action}) is still running; read it with action:status");
            string[] known = { "list", "active", "activate", "create", "clone", "enable" };
            if (!known.Contains(action))
                throw new ArgumentException($"unknown action '{action}' (list, active, activate, create, clone, enable, status)");
            if ((action == "create" || action == "clone") && !(((string)args["name"]) ?? "").StartsWith(kTestPlaysetPrefix, StringComparison.Ordinal))
                throw new ArgumentException($"refused: only playsets named \"{kTestPlaysetPrefix}...\" may be created");

            var job = new PlaysetJob { action = action, startedAt = Now };
            m_PlaysetJob = job;
            // Started on the main thread; the continuations of the SDK's tasks come back through its synchronization context.
            PlaysetRun(job, args);
            return new JObject { ["state"] = "running", ["action"] = action, ["note"] = "poll {op:playset, action:status}" };
        }

        private PdxSdkPlatform GetPdxPlatform()
        {
            return PlatformManager.instance.GetPSI<PdxSdkPlatform>("PdxSdk") ?? throw new InvalidOperationException("PdxSdkPlatform not registered");
        }

        private IContext GetPdxContext(PdxSdkPlatform psi)
        {
            FieldInfo f = typeof(PdxSdkPlatform).GetField("m_SDKContext", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("PdxSdkPlatform.m_SDKContext not found (game patch?)");
            return (IContext)f.GetValue(psi) ?? throw new InvalidOperationException("the Paradox SDK context is not up yet (still starting, or offline?)");
        }

        private static JObject PlaysetJson(Playset p, string activeId)
        {
            return new JObject
            {
                ["id"] = p.Id, ["name"] = p.Name, ["state"] = p.State.ToString(), ["source"] = p.Source.ToString(),
                ["mods"] = p.ModsCount, ["ownership"] = p.Ownership.ToString(), ["active"] = p.Id == activeId,
            };
        }

        private static string ResultError(object result) => (result as IResult)?.Error?.ToString() ?? "unknown error";

        private async void PlaysetRun(PlaysetJob job, JObject args)
        {
            try
            {
                PdxSdkPlatform psi = GetPdxPlatform();
                IContext ctx = GetPdxContext(psi);
                var mods = ctx.Mods;
                var res = new JObject();

                // The active playset (always read first: every action reports it).
                IGetActivePlaysetResult act = await mods.GetActivePlayset();
                string activeId = act.Success ? act.Playset?.Id : null;
                string activeName = act.Success ? act.Playset?.Name : null;
                res["active"] = act.Success ? PlaysetJson(act.Playset, activeId) : null;

                IListAllPlaysetsResult all = await mods.ListAllPlaysets(false, false);
                if (!all.Success) throw new InvalidOperationException("ListAllPlaysets failed: " + ResultError(all));
                List<Playset> sets = all.AllPlaysets ?? new List<Playset>();

                Playset Find(string name, string id)
                {
                    if (!string.IsNullOrEmpty(id)) return sets.FirstOrDefault(p => p.Id == id);
                    var m = sets.Where(p => p.Name == name).ToList();
                    if (m.Count > 1) throw new InvalidOperationException($"{m.Count} playsets are named '{name}': pass id");
                    return m.FirstOrDefault();
                }

                switch (job.action)
                {
                    case "list":
                        res["playsets"] = new JArray(sets.Select(p => PlaysetJson(p, activeId)));
                        break;
                    case "active":
                        break;
                    case "activate":
                    {
                        Playset p = Find((string)args["name"], (string)args["id"]) ?? throw new InvalidOperationException("no such playset (name/id)");
                        if (p.Id == activeId) { res["note"] = "already active"; break; }
                        IModResult r = await mods.ActivatePlayset(p.Id);
                        if (!r.Success) throw new InvalidOperationException("ActivatePlayset failed: " + ResultError(r));
                        res["activated"] = PlaysetJson(p, p.Id);
                        res["note"] = "active playset switched; restart the game before any test (the mod set loads at the start)";
                        break;
                    }
                    case "create":
                    case "clone":
                    {
                        string name = (string)args["name"];
                        Playset existing = Find(name, null);
                        if (existing != null) { res["reused"] = PlaysetJson(existing, activeId); break; }
                        ICreatePlaysetResult cr;
                        if (job.action == "create") cr = await mods.CreatePlayset(name);
                        else
                        {
                            Playset src = Find((string)args["from"], (string)args["fromId"]) ?? throw new InvalidOperationException("no such source playset (from/fromId)");
                            cr = await mods.ClonePlayset(src.Id, name);
                            res["clonedFrom"] = src.Name;
                        }
                        if (((IPartialResult)cr).Success == ResultStatus.Failure) throw new InvalidOperationException($"{job.action} failed: " + ResultError(cr));
                        res["created"] = new JObject { ["id"] = cr.Id, ["name"] = cr.Name };
                        break;
                    }
                    case "enable":
                    {
                        if (!act.Success || !(activeName ?? "").StartsWith(kTestPlaysetPrefix, StringComparison.Ordinal))
                            throw new InvalidOperationException($"refused: the active playset is '{activeName}'; mods are switched only in a playset named \"{kTestPlaysetPrefix}...\"");
                        string modId = (string)args["modId"] ?? throw new ArgumentException("modId");
                        bool enable = (bool?)args["enable"] ?? true;
                        bool ok = await psi.EnableModInActivePlayset(modId, enable);
                        if (!ok) throw new InvalidOperationException($"{(enable ? "Enable" : "Disable")} {modId} failed in '{activeName}'");
                        res["mod"] = modId; res["enabled"] = enable; res["playset"] = activeName;
                        break;
                    }
                }
                job.result = res;
                job.state = "done";
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"playset {job.action}: {e}");
                job.error = e.GetType().Name + ": " + e.Message;
                job.state = "failed";
            }
        }
    }
}
