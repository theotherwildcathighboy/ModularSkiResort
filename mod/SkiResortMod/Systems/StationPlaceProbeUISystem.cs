// Dev-only triggers of the station placer (the P0 probe of the Resort Planner round) and of the planner's lift build (task R1-c). Two
// TriggerBindings, group "skiResortDev" (not the hub's contract: bindings.ts never calls them, and tools/check-bundle.js reads only
// SkiResortHubUISystem.cs):
//   engine.trigger("skiResortDev.probePlaceStation", "<station prefab> <x> <z> <yaw deg> [<y>]")
//   engine.trigger("skiResortDev.planLifts", "<command>"), commands (ResortLiftBuildSystem):
//     "build all" | "build <row name or hex key>" | "stop" | "clear" | "dump"
//     "height vanilla" | "height lowest": the station height rule of the queue (StationAlign.StationHeightRule), to compare in game
//     "line <lift type id> <x1> <z1> <x2> <z2> [manual]": a scripted ground-to-ground line (LiftLineToolSystem.TryActivateForGround),
//     auto build unless "manual"
// cs2/api trigger(group, name, ...args) is engine.trigger(`${group}.${name}`, ...args) in the game's UI bundle. One string argument because
// TriggerBinding takes at most four (Colossal.UI.Binding has TriggerBinding`1..`4). Numbers are invariant culture. y absent or NaN = the
// vanilla height (StationPlaceToolSystem.ResolveY). Yaw: the direction of the station's local +z, 0 = +z, 90 = +x (quaternion.RotateY). The
// line leaves the station along its local -z (StationAlign.cs:142-148).
// It acts only with Options > Diagnostics > Measurement diagnostics on. Otherwise every call is refused with a log line.
// The trigger body only queues; OnUpdate (UIUpdate, main thread) parses and calls StationPlaceToolSystem.TryPlace or the lift build's
// entry points, the hub's idiom (a command buffer from a trigger callback throws). OnUpdate also ticks the placer's follow-up line (an inactive tool never ticks).
// Logs (silence impossible): one line per call (accepted with the parsed pose, or refused with the reason). An exception disables the
// trigger until the next load.
// Engine notes: EngineNotes/StationPlaceToolSystem.md (Dev triggers)

using System;
using System.Collections.Generic;
using System.Globalization;
using Colossal.Serialization.Entities;
using Colossal.UI.Binding;
using Game;
using Game.UI;
using SkiResortMod.Settings;
using Unity.Entities;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class StationPlaceProbeUISystem : UISystemBase
    {
        /// <summary>Binding group of dev-only triggers (never part of the hub contract).</summary>
        public const string Group = "skiResortDev";

        private StationPlaceToolSystem m_Placer;
        private SkiPrefabCatalogSystem m_Catalog;
        private ResortLiftBuildSystem m_LiftBuild;
        private LiftLineToolSystem m_LiftTool;
        private readonly List<string> m_Queue = new List<string>();
        private readonly List<string> m_PlanQueue = new List<string>();
        private bool m_Disabled, m_ErrorLogged;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Placer = World.GetOrCreateSystemManaged<StationPlaceToolSystem>();
            m_Catalog = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>();
            m_LiftBuild = World.GetOrCreateSystemManaged<ResortLiftBuildSystem>();
            m_LiftTool = World.GetOrCreateSystemManaged<LiftLineToolSystem>();
            AddBinding(new TriggerBinding<string>(Group, "probePlaceStation", OnProbePlaceStation));
            AddBinding(new TriggerBinding<string>(Group, "planLifts", OnPlanLifts));
            Mod.Log.Info($"{nameof(StationPlaceProbeUISystem)} created (triggers {Group}.probePlaceStation and {Group}.planLifts, Measurement " +
                         "diagnostics only)");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Disabled = m_ErrorLogged = false;
            m_Queue.Clear();
            m_PlanQueue.Clear();
        }

        private void OnPlanLifts(string command)
        {
            try
            {
                if (m_PlanQueue.Count < 8)
                    m_PlanQueue.Add(command ?? string.Empty);
                else
                    Mod.Log.Warn($"ResortLifts [dev]: call '{command}' dropped — 8 calls already queued");
            }
            catch (Exception e)
            {
                Fail(e, "planLifts trigger");
            }
        }

        private void OnProbePlaceStation(string spec)
        {
            try
            {
                if (m_Queue.Count < 8)
                    m_Queue.Add(spec ?? string.Empty);
                else
                    Mod.Log.Warn($"StationPlace [probe]: call '{spec}' dropped — 8 calls already queued");
            }
            catch (Exception e)
            {
                Fail(e, "trigger");
            }
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();
            if (m_Disabled)
                return;
            try
            {
                m_Placer.TickWatch();
                if (m_PlanQueue.Count > 0)
                {
                    string[] commands = m_PlanQueue.ToArray();
                    m_PlanQueue.Clear();
                    foreach (string command in commands)
                        HandlePlan(command);
                }
                if (m_Queue.Count == 0)
                    return;
                string[] calls = m_Queue.ToArray();
                m_Queue.Clear();
                foreach (string spec in calls)
                    Handle(spec);
            }
            catch (Exception e)
            {
                Fail(e, "OnUpdate");
            }
        }

        private void Handle(string spec)
        {
            if (!SkiResortSettings.DiagnosticsOn)
            {
                Mod.Log.Warn($"StationPlace [probe]: '{spec}' REFUSED — Measurement diagnostics is off (Options > Diagnostics)");
                return;
            }
            if (!TryParse(spec, out string name, out float3 position, out float yawDeg, out string error))
            {
                Mod.Log.Warn($"StationPlace [probe]: '{spec}' REFUSED — {error}; expected \"<station prefab> <x> <z> <yaw deg> [<y>]\"");
                return;
            }
            if (SkiCatalog.StationByPrefab(name) == null)
            {
                Mod.Log.Warn($"StationPlace [probe]: '{spec}' REFUSED — '{name}' is not a station row of SkiCatalog");
                return;
            }
            Entity prefab = m_Catalog.EntityOf(name);
            if (prefab == Entity.Null)
            {
                Mod.Log.Warn($"StationPlace [probe]: '{spec}' REFUSED — the prefab '{name}' is MISSING this load");
                return;
            }
            quaternion rotation = quaternion.RotateY(math.radians(yawDeg));
            bool ok = m_Placer.TryPlace("probe trigger", prefab, position, rotation);
            Mod.Log.Info($"StationPlace [probe]: '{spec}' {(ok ? "accepted" : "refused by the placer")} — result #{m_Placer.LastResult.Serial}: " +
                         m_Placer.LastResult.Summary);
        }

        private void HandlePlan(string command)
        {
            if (!SkiResortSettings.DiagnosticsOn)
            {
                Mod.Log.Warn($"ResortLifts [dev]: '{command}' REFUSED — Measurement diagnostics is off (Options > Diagnostics)");
                return;
            }
            if (!m_LiftBuild.IsArmed)
            {
                Mod.Log.Warn($"ResortLifts [dev]: '{command}' REFUSED — the lift build is not armed (not in a game, or disabled after an exception)");
                return;
            }
            string[] parts = (command ?? string.Empty).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            string verb = parts.Length > 0 ? parts[0].ToLowerInvariant() : string.Empty;
            Mod.Log.Info($"ResortLifts [dev]: '{command}' accepted");
            if (verb == "build" && parts.Length == 2 && parts[1].Equals("all", StringComparison.OrdinalIgnoreCase))
                m_LiftBuild.BuildAll("dev trigger");
            else if (verb == "build" && parts.Length == 2)
            {
                if (TryRowKey(parts[1], out uint key))
                    m_LiftBuild.BuildOne(key, "dev trigger");
                else
                    Mod.Log.Warn($"ResortLifts [dev]: '{command}' — no sheet row named or keyed '{parts[1]}'; {m_LiftBuild.Sheet.Count} row(s)");
            }
            else if (verb == "stop" && parts.Length == 1)
                m_LiftBuild.Stop("dev trigger");
            else if (verb == "clear" && parts.Length == 1)
                m_LiftBuild.ClearSheet("dev trigger");
            else if (verb == "dump" && parts.Length == 1)
                m_LiftBuild.DumpSheet("dev trigger");
            else if (verb == "line" && (parts.Length == 6 || parts.Length == 7))
                GroundLine(command, parts);
            else if (verb == "height" && parts.Length == 2 && (parts[1].Equals("vanilla", StringComparison.OrdinalIgnoreCase) ||
                                                              parts[1].Equals("lowest", StringComparison.OrdinalIgnoreCase)))
                m_LiftBuild.SetStationHeight(parts[1].Equals("vanilla", StringComparison.OrdinalIgnoreCase)
                    ? StationAlign.StationHeightRule.VanillaFront : StationAlign.StationHeightRule.LowestOfThree, "dev trigger");
            else
                Mod.Log.Warn($"ResortLifts [dev]: '{command}' REFUSED — expected \"build all\", \"build <row name or hex key>\", \"stop\", \"clear\", " +
                             "\"dump\", \"height vanilla|lowest\" or \"line <lift type id> <x1> <z1> <x2> <z2> [manual]\"");
        }

        /// <summary>A row by its name ("P1", any case) or its hex key.</summary>
        private bool TryRowKey(string token, out uint key)
        {
            foreach (ResortPlanSheet.Row r in m_LiftBuild.Sheet.Rows)
                if (string.Equals(r.Name, token, StringComparison.OrdinalIgnoreCase))
                {
                    key = r.Key;
                    return true;
                }
            return uint.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out key) && m_LiftBuild.Sheet.Find(key) != null;
        }

        /// <summary>"line &lt;lift type id&gt; x1 z1 x2 z2 [manual]": a scripted ground-to-ground line, y from the terrain.</summary>
        private void GroundLine(string command, string[] parts)
        {
            LiftTypeDef type = SkiCatalog.LiftById(parts[1]);
            bool manual = parts.Length == 7 && parts[6].Equals("manual", StringComparison.OrdinalIgnoreCase);
            if (type == null || !Num(parts[2], out float x1) || !Num(parts[3], out float z1) || !Num(parts[4], out float x2) ||
                !Num(parts[5], out float z2) || (parts.Length == 7 && !manual))
            {
                Mod.Log.Warn($"ResortLifts [dev]: '{command}' REFUSED — " + (type == null ? $"'{parts[1]}' is not a lift type id" : "a field does not parse"));
                return;
            }
            LiftLineToolSystem.ScriptTicket t = m_LiftTool.TryActivateForGround("dev trigger", type, new float3(x1, float.NaN, z1),
                new float3(x2, float.NaN, z2), autoBuild: !manual);
            Mod.Log.Info($"ResortLifts [dev]: scripted ground line #{t.Serial} {t.Outcome}{(t.Reason != null ? " — " + t.Reason : string.Empty)}");
        }

        /// <summary>"name x z yaw [y]": whitespace-separated, invariant culture. A missing y, or "NaN", is NaN.</summary>
        internal static bool TryParse(string spec, out string name, out float3 position, out float yawDeg, out string error)
        {
            name = null;
            position = default;
            yawDeg = 0f;
            string[] parts = (spec ?? string.Empty).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4 || parts.Length > 5)
            {
                error = $"{parts.Length} field(s)";
                return false;
            }
            name = parts[0];
            float y = float.NaN;
            if (!Num(parts[1], out float x) || !Num(parts[2], out float z) || !Num(parts[3], out yawDeg) ||
                (parts.Length == 5 && !Num(parts[4], out y)))
            {
                error = "a number does not parse";
                return false;
            }
            if (!math.isfinite(x) || !math.isfinite(z) || !math.isfinite(yawDeg))
            {
                error = "x, z and yaw must be finite";
                return false;
            }
            position = new float3(x, y, z);
            error = null;
            return true;
        }

        private static bool Num(string s, out float v) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);

        private void Fail(Exception e, string where)
        {
            m_Disabled = true;
            if (!m_ErrorLogged)
            {
                m_ErrorLogged = true;
                Mod.Log.Error($"!!! {nameof(StationPlaceProbeUISystem)} EXCEPTION in {where} — the probe trigger is disabled until the next game " +
                              $"load. Please report this stack:\n{e}");
            }
        }
    }
}
