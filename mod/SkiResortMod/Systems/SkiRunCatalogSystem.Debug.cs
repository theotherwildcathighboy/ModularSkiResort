// The "Ski Resort Hub" tab of the game's developer menu: catalog, rating-cache and rating-service rows, the SkiResortStatsSystem and
// hub rows, and action buttons. Pattern of ChairPinSystem.Debug.cs / SkiGladeToolSystem.Debug.cs. Game.Debug.DebugSystem invokes each
// [DebugTab] method on the live instance (DebugSystem.cs:996-1045), hence a partial of the catalog; nothing is registered in Mod.cs.
// The tab name is load-bearing: AddPanel keys widget lists by tab name and replaces a previous list of the same name
// (DebugSystem.cs:1085-1095); "Ski Resort" (ChairPinSystem), "Ski Glades" and "Ski Runs" are taken.
// DebugUI.Value getters run at 10 Hz while the tab is open: every row reads plain fields or builds one display string; no EntityQuery,
// no job completion. Buttons go through the hub's request queue (SkiResortHubUISystem.DevRequest). Tool mouse input is blocked while
// the cursor is over this panel (ToolSystem.cs:203-222): after a launch, move the cursor onto the map.

using System;
using System.Collections.Generic;
using Game.Debug;
using Unity.Entities;
using UnityEngine.Rendering;

namespace SkiResortMod.Systems
{
    [DebugContainer]
    public sealed partial class SkiRunCatalogSystem
    {
        [DebugTab("Ski Resort Hub", 3)]
        private List<DebugUI.Widget> BuildSkiResortHubDebugUI()
        {
            var list = new List<DebugUI.Widget>();
            SkiResortHubUISystem hub = World.GetExistingSystemManaged<SkiResortHubUISystem>();

            list.Add(HubVal("Hub armed / disabled / open", () => hub == null
                ? "hub system not registered"
                : $"{hub.IsArmed} / {hub.IsDisabled} / {hub.IsOpen}"));
            list.Add(HubVal("Contract / DLL build stamp", () =>
                $"{SkiResortHubUISystem.HubContract} / {BuildStamp.Value} (UI bundled at build: {BuildStamp.UiBundled})"));
            list.Add(HubVal("Handshake (last uiHello)", () => hub == null ? "-" : hub.HandshakeText));
            list.Add(HubVal("Observers (JS binding objects)", () => hub == null ? "-" : hub.ObserverText()));
            list.Add(HubVal("Last trigger", () => hub == null ? "-" : hub.LastTriggerText));
            list.Add(HubVal("Triggers / runs pushes / UI error repeats", () => hub == null
                ? "-"
                : $"{hub.TriggerCount} / {hub.RunsPushes} / {hub.UiErrorRepeats}"));
            list.Add(HubVal("Active tool / selection", () => hub == null ? "-" : hub.ToolText()));
            list.Add(HubVal("Hub v3: piste map / follows / hovers", () => hub == null
                ? "-"
                : $"{(hub.PisteMapOn ? "on" : "off")} (visible {hub.PisteMapVisible}) / {hub.FollowCount} / {hub.HoverAccepted} (refused {hub.HoverRefused})"));
            SkiPisteMapSystem piste = World.GetExistingSystemManaged<SkiPisteMapSystem>();
            list.Add(HubVal("Piste map overlay", () => piste == null ? "piste map system not registered" : piste.StateText()));

            list.Add(HubVal("Catalog armed / disabled / demand", () => $"{IsArmed} / {IsDisabled} / {Demand}"));
            list.Add(HubVal("Last rebuild (why, ms, max ms)", () =>
                $"{LastRebuildWhy}, {LastRebuildMs:F2} ms, max {MaxRebuildMs:F2} ms"));
            list.Add(HubVal("Rebuilds (changed) / revision", () => $"{Rebuilds} ({RebuildsChanged}) / {Revision}"));
            list.Add(HubVal("Rows: runs (linked / not linked)", () => $"{RowCount} ({RunsBound} / {RunsUnbound})"));
            list.Add(HubVal("Signs / area m²", () => $"{Signs} / {AreaM2:F0}"));
            list.Add(HubVal("First row", () =>
            {
                if (RowCount == 0)
                    return "-";
                RunRow row = GetRow(0);
                return $"\"{row.Name}\" kind {row.Kind}, {(row.Bound ? "sign " + row.Gate.Index + ":" + row.Gate.Version : "NOT linked")}, " +
                       $"rate state {row.RateState} band {row.Band}, length {row.LengthM:F0} m, drop {row.DropM:F0} m" +
                       $"{(row.Approx ? " (node estimates)" : "")}, area {row.AreaM2:F0} m², entity {row.Entity.Index}:{row.Entity.Version}";
            }));

            list.Add(HubVal("Ratings: rated / rows / queued / done", () => $"{RatedCount} / {RowCount} / {RatingsPending} / {Ratings}"));
            list.Add(HubVal("Rating ms (last / max) / terrain epoch", () => $"{LastRateMs:F1} / {MaxRateMs:F1} / {TerrainEpoch}"));
            list.Add(HubVal("Last rating", () => LastRateLine));
            list.Add(HubVal("Rating service", () =>
                $"{ServiceState}, revision {RatingRevision}, closed rebuilds {m_ClosedRebuilds}, closed ratings {m_ClosedRatings}"));

            SkiResortStatsSystem stats = World.GetExistingSystemManaged<SkiResortStatsSystem>();
            list.Add(HubVal("Stats armed / disabled / demand", () => stats == null
                ? "stats system not registered"
                : $"{stats.IsArmed} / {stats.IsDisabled} / {stats.Demand}"));
            list.Add(HubVal("Stats last rebuild (why, ms, max ms) / rebuilds", () => stats == null
                ? "-"
                : $"{stats.LastRebuildWhy}, {stats.LastRebuildMs:F2} ms, max {stats.MaxRebuildMs:F2} ms / {stats.Rebuilds}"));
            list.Add(HubVal("Stats revisions resorts/live/runs/runLive/lifts/problems", () => stats == null
                ? "-"
                : $"{stats.ResortsRevision}/{stats.ResortLiveRevision}/{stats.RunsRevision}/{stats.RunLiveRevision}/{stats.LiftsRevision}/{stats.ProblemsRevision}"));
            list.Add(HubVal("Stats rows resorts/runs/lifts/problems", () => stats == null
                ? "-"
                : $"{stats.ResortCount}/{stats.RunCount}/{stats.LiftCount}/{stats.ProblemCount}"));
            list.Add(HubVal("Stats first resort", () =>
            {
                if (stats == null || stats.ResortCount == 0 || stats.ResortLiveCount == 0)
                    return "-";
                ResortStats r = stats.GetResort(0);
                ResortLiveStats l = stats.GetResortLive(0);
                return $"'{r.Name}' runs {r.Runs} (rated {r.Rated}), lift rows {r.LiftRows}, lodges {r.Lodges}; A {l.Attraction} " +
                       $"(full {l.AttractionFull}), efficiency {l.EfficiencyPct} %, status {l.Status} ({l.RunsOpen} open, " +
                       $"{l.RunsPartly} partly), snow {l.SnowPct:F0} %, guests {l.GuestsThisMonth}, net {l.NetMonth} this month";
            }));
            list.Add(HubButton("Rebuild stats now", () => stats?.RequestRebuild("dev tab")));
            list.Add(HubVal("Note", () => "after a launch button, move the cursor onto the map"));

            list.Add(HubButton("Dump hub + catalog to mod log", () =>
            {
                RequestRebuild("dev tab dump");
                World.GetExistingSystemManaged<SkiResortStatsSystem>()?.RequestRebuild("dev tab dump");
                hub?.DevRequest("dump", SkiResortHubUISystem.DevKindDump, null, Entity.Null);
            }));
            list.Add(HubButton("Rebuild catalog now", () => RequestRebuild("dev tab")));
            list.Add(HubButton("Open / close hub", () =>
                hub?.DevRequest("toggleHub", SkiResortHubUISystem.DevKindToggleHub, null, Entity.Null)));
            list.Add(HubButton("Focus first run", () =>
            {
                if (RowCount == 0)
                {
                    Mod.Log.Info("Hub: [dev tab] focus first run — the catalog has no rows (open the hub or rebuild first)");
                    return;
                }
                hub?.DevRequest("focusRun", SkiResortHubUISystem.DevKindFocus, null, GetRow(0).Entity);
            }));
            list.Add(HubButton("Glade tool on / off", () =>
                hub?.DevRequest("gladeToggle", SkiResortHubUISystem.DevKindGladeToggle, null, Entity.Null)));
            IReadOnlyList<string> ids = SkiResortHubUISystem.BuildItemIds;
            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                list.Add(HubButton("Launch: " + id, () =>
                    hub?.DevRequest("launch " + id, SkiResortHubUISystem.DevKindLaunch, id, Entity.Null)));
            }
            list.Add(HubButton("Piste map on / off", () =>
                hub?.DevRequest("togglePisteMap", SkiResortHubUISystem.DevKindTogglePisteMap, null, Entity.Null)));
            list.Add(HubButton("Refresh tab", () => DebugSystem.Rebuild(BuildSkiResortHubDebugUI)));

            return list;
        }

        /// <summary>A button body runs inside the debug UI's invoke path: never throw into it, never fail silently.</summary>
        private static DebugUI.Button HubButton(string name, Action body)
        {
            return new DebugUI.Button
            {
                displayName = name,
                action = () =>
                {
                    try
                    {
                        body();
                    }
                    catch (Exception e)
                    {
                        Mod.Log.Error($"Hub: [dev tab] '{name}' failed:\n{e}");
                    }
                },
            };
        }

        /// <summary>A getter that cannot throw into the 10 Hz refresh (ChairPinSystem.Debug pattern).</summary>
        private static DebugUI.Value HubVal(string name, Func<object> getter)
        {
            return new DebugUI.Value
            {
                displayName = name,
                getter = () =>
                {
                    try
                    {
                        return getter();
                    }
                    catch (Exception e)
                    {
                        string s = e.ToString();
                        int nl = s.IndexOf('\n');
                        return "err: " + (nl < 0 ? s : s.Substring(0, nl).TrimEnd('\r'));
                    }
                },
            };
        }
    }
}
