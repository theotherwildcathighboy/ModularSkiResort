// SkiRunCatalogSystem.Debug — the "Ski Resort Hub" tab of the game's developer menu
// (docs/research/resort-hub-understand.md §5.2 item 4: observer count, last rebuild ms, rows, last
// trigger, bundle stamp; §2.G.2). Created 2026-10-01 (Round U1, implementer U1-B). The pattern of
// ChairPinSystem.Debug.cs / SkiGladeToolSystem.Debug.cs. Round U2 (hub-u2-spec.md §3.5, §3.6;
// 2026-10-03, implementer I-1): rating-cache rows (rated / queued / done, rating ms, terrain epoch,
// the last rating) and the SkiResortStatsSystem rows (state, rebuild ms, revisions, row counts, the
// first resort) plus a "Rebuild stats now" button; the retired "lifts unknown, U3" value is gone.
// Round E1 (stream S2, 2026-10-04): the "Rating service" row (docs/research/econ-round/spec/ch2-runs.md
// §2.3: state, rating revision, panel-closed rebuilds and ratings) and, verbatim, the hub chapter's
// requested change R-C2-1 (spec/ch5-hub-csharp.md §14): contract 3's stats rows (revisions resorts /
// live / runs / runLive / lifts / problems, rows, the first resort's live values), the two hub v3 rows
// (piste map / follows / hovers, the piste-map overlay's StateText) and the "Piste map on / off" button.
// They read stream S5's members (SkiResortStatsSystem, SkiResortHubUISystem, SkiPisteMapSystem).
//
// HOW IT REACHES THE SCREEN. Game.Debug.DebugSystem sweeps every [DebugContainer] type and invokes
// each [DebugTab] method on the live system instance (ExecuteMethod / RegisterDebug,
// DebugSystem.cs:996-1045, same lines in 1.6.2), which is why this file is a partial of the catalog.
// Nothing is registered in Mod.cs for the tab. DebugSystem.Rebuild(Func<List<Widget>>) :971.
//
// TAB NAME IS LOAD-BEARING: "Ski Resort Hub" is unique. AddPanel keys widget lists by tab name and
// REMOVES a previously registered list of the same name (DebugSystem.cs:1085-1095, unchanged in
// 1.6.2): "Ski Resort" (ChairPinSystem), "Ski Glades" and "Ski Runs" are taken.
//
// COST CONTRACT: DebugUI.Value getters run at 10 Hz while the tab is open. Every row reads plain
// fields or builds one display string; no EntityQuery, no job completion. The hub rows read the hub
// system's public diagnostics (field-backed). Buttons go through the hub's own request queue
// (SkiResortHubUISystem.DevRequest), so a dev-tab launch / focus / dump is applied, logged and
// guarded exactly like the UI's trigger — the way to test C# before the TSX exists (§2.C last
// paragraph). Tool mouse input is blocked while the cursor is over this panel (ToolSystem.cs:203-222):
// after a launch, move the cursor onto the map.

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

            // ── Hub (SkiResortHubUISystem) ──────────────────────────────────────────────────────
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
            list.Add(HubVal("Lodge launch", () => hub == null ? "-" : hub.LodgeStateText));
            // Round E1 (econ-round spec ch5 §14 R-C2-1 (b), verbatim): the hub v3 rows.
            list.Add(HubVal("Hub v3: piste map / follows / hovers", () => hub == null
                ? "-"
                : $"{(hub.PisteMapOn ? "on" : "off")} (visible {hub.PisteMapVisible}) / {hub.FollowCount} / {hub.HoverAccepted} (refused {hub.HoverRefused})"));
            SkiPisteMapSystem piste = World.GetExistingSystemManaged<SkiPisteMapSystem>();
            list.Add(HubVal("Piste map overlay", () => piste == null ? "piste map system not registered" : piste.StateText()));

            // ── Catalog (this system) ───────────────────────────────────────────────────────────
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

            // ── U2 rating cache (hub-u2-spec.md §3.5) ──────────────────────────────────────────
            list.Add(HubVal("Ratings: rated / rows / queued / done", () => $"{RatedCount} / {RowCount} / {RatingsPending} / {Ratings}"));
            list.Add(HubVal("Rating ms (last / max) / terrain epoch", () => $"{LastRateMs:F1} / {MaxRateMs:F1} / {TerrainEpoch}"));
            list.Add(HubVal("Last rating", () => LastRateLine));
            // Round E1 (ch2 §2.3): the always-on rating service (plain field reads).
            list.Add(HubVal("Rating service", () =>
                $"{ServiceState}, revision {RatingRevision}, closed rebuilds {m_ClosedRebuilds}, closed ratings {m_ClosedRatings}"));

            // ── U2 stats (SkiResortStatsSystem, hub-u2-spec.md §3.6) ───────────────────────────
            SkiResortStatsSystem stats = World.GetExistingSystemManaged<SkiResortStatsSystem>();
            list.Add(HubVal("Stats armed / disabled / demand", () => stats == null
                ? "stats system not registered"
                : $"{stats.IsArmed} / {stats.IsDisabled} / {stats.Demand}"));
            list.Add(HubVal("Stats last rebuild (why, ms, max ms) / rebuilds", () => stats == null
                ? "-"
                : $"{stats.LastRebuildWhy}, {stats.LastRebuildMs:F2} ms, max {stats.MaxRebuildMs:F2} ms / {stats.Rebuilds}"));
            // Round E1 (econ-round spec ch5 §14 R-C2-1 (a), verbatim): contract 3's stats rows.
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

            // ── Actions (through the hub's request queue) ───────────────────────────────────────
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
            // Round E1 (econ-round spec ch5 §14 R-C2-1 (c), verbatim).
            list.Add(HubButton("Piste map on / off", () =>
                hub?.DevRequest("togglePisteMap", SkiResortHubUISystem.DevKindTogglePisteMap, null, Entity.Null)));
            list.Add(HubButton("Refresh tab", () => DebugSystem.Rebuild(BuildSkiResortHubDebugUI)));

            return list;
        }

        /// <summary>A button body runs inside the debug UI's invoke path: never throw into it, never
        /// fail silently.</summary>
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
