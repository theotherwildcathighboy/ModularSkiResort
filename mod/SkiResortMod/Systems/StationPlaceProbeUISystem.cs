// Dev-only trigger of the station placer (the P0 probe of the Resort Planner round). One TriggerBinding, group "skiResortDev" (not the
// hub's contract: bindings.ts never calls it, and tools/check-bundle.js reads only SkiResortHubUISystem.cs):
//   engine.trigger("skiResortDev.probePlaceStation", "<station prefab> <x> <z> <yaw deg> [<y>]")
// cs2/api trigger(group, name, ...args) is engine.trigger(`${group}.${name}`, ...args) in the game's UI bundle. One string argument because
// TriggerBinding takes at most four (Colossal.UI.Binding has TriggerBinding`1..`4). Numbers are invariant culture. y absent or NaN = the
// vanilla height (StationPlaceToolSystem.ResolveY). Yaw: the direction of the station's local +z, 0 = +z, 90 = +x (quaternion.RotateY). The
// line leaves the station along its local -z (StationAlign.cs:142-148).
// It acts only with Options > Diagnostics > Measurement diagnostics on. Otherwise every call is refused with a log line.
// The trigger body only queues; OnUpdate (UIUpdate, main thread) parses and calls StationPlaceToolSystem.TryPlace, the hub's idiom (a command
// buffer from a trigger callback throws). OnUpdate also ticks the placer's follow-up line (an inactive tool never ticks).
// Logs (silence impossible): one line per call (accepted with the parsed pose, or refused with the reason). An exception disables the
// trigger until the next load.

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
        private readonly List<string> m_Queue = new List<string>();
        private bool m_Disabled, m_ErrorLogged;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Placer = World.GetOrCreateSystemManaged<StationPlaceToolSystem>();
            m_Catalog = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>();
            AddBinding(new TriggerBinding<string>(Group, "probePlaceStation", OnProbePlaceStation));
            Mod.Log.Info($"{nameof(StationPlaceProbeUISystem)} created (trigger {Group}.probePlaceStation, Measurement diagnostics only)");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Disabled = m_ErrorLogged = false;
            m_Queue.Clear();
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
