// SkiGladeToolSystem.Debug - the "Ski Glades" tab of the game's developer menu (the ChairPinSystem.Debug.cs pattern).
//
// Tab methods on a ComponentSystemBase run on the live system instance (DebugSystem.cs:996-1024), which is why this file is a
// partial of the tool. Nothing is registered in Mod.cs.
// The tab name is load-bearing: AddPanel keys widget lists by tab name and replaces an existing list (DebugSystem.cs:1085-1095),
// so a second "Ski Resort" tab would wipe ChairPinSystem's tab.
// DebugUI.Value getters run at 10 Hz: every row reads a plain field, no EntityQuery or job completion; heavy work is a button.
// Engine notes: EngineNotes/SkiGladeToolSystem.Debug.md

using System;
using System.Collections.Generic;
using Game;
using Game.Debug;
using Game.SceneFlow;
using Unity.Entities;
using UnityEngine.Rendering;

namespace SkiResortMod.Systems
{
    [DebugContainer]
    public sealed partial class SkiGladeToolSystem
    {
        /// <summary>Optional hook for the tool's main file: write the resolved species pool (e.g. "SpruceTree01 (dominant),
        /// PineTree01, BirchTree01") into <paramref name="text"/> from a field cached at ARMED time. Must be field-read only
        /// (10 Hz cost). When unimplemented, the call below compiles away and the row keeps its fallback.</summary>
        partial void DescribeSpeciesPoolForDebug(ref string text);

        [DebugTab("Ski Glades", 1)]
        private List<DebugUI.Widget> BuildSkiGladesDebugUI()
        {
            var list = new List<DebugUI.Widget>();

            list.Add(Val("Armed", () => IsArmed));
            list.Add(Val("Disabled by exception", () => IsDisabled));
            list.Add(Val("Active (ToolSystem.activeTool == this)", () => IsActive));
            list.Add(Val("Target (Alt+G)", () => TreeLineTarget ? "Tree line" : "Run interior"));
            list.Add(Val("Tree density level (Shift+G, 1..8)", () => $"{Level} — {LevelName(Level)}"));
            list.Add(Val("Planned run: target / level / layer", () =>
                m_PlannedRun == Entity.Null
                    ? "-"
                    : $"{(m_PlannedTreeLine ? "Tree line" : "Interior")} / L{m_PlannedLevel} / {LayerText(m_PlannedLayer)}"));
            list.Add(Val("Plan counts: target / now (wild, fixed, own) / keep", () =>
                PlanHasCounts
                    ? $"{PlanTarget} / {PlanNow} ({PlanWild}, {PlanFixed}, {PlanOwn}) / {PlanKeep}"
                    : "-"));
            list.Add(Val("Census Overridden trees (inside / band, counted)", () =>
                $"{m_CenOverriddenInside} / {m_CenOverriddenBand}"));
            list.Add(Val("Last apply check", () => LastApplyCheck ?? "-"));
            list.Add(Val("Hovered run", () =>
                HoveredRun == Entity.Null ? "-" : GladeDebugEnt(HoveredRun)));
            list.Add(Val("Hover kind / detail", () => $"{HoverKind} — {HoverStatus ?? "-"}"));
            list.Add(Val("Input live (controlOverWorld, no UI/debug disable)", () => m_InputLive));
            list.Add(Val("Plan state", () => PlanState.ToString()));
            list.Add(Val("Preview plant / canceled / thin / remove / canceled deletes / cost / errors", () =>
                $"{PreviewPlant} / {PreviewCancelled} / {PreviewThin} / {PreviewRemove} / {PreviewDeleteCancelled} / " +
                $"{PreviewCost} / {PreviewErrors}"));
            list.Add(Val("Blocked spots known (this plan)", () => PreviewBlockedKnown));
            list.Add(Val("Bound runs in city (live / at activation)", () =>
                $"{CityBoundRuns} / {(m_BoundRunsAtActivation < 0 ? "-" : m_BoundRunsAtActivation.ToString())}"));
            list.Add(Val("Hover transitions logged / suppressed (cap 40)", () =>
                $"{m_HoverLog.Logged} / {m_HoverLog.Suppressed}"));
            list.Add(Val("Bound via point test (this activation)", () => m_HoverLog.ViaPoint));
            list.Add(Val("Point scan µs (last / max)", () =>
                $"{m_LastPointScanUs:F0} / {m_MaxPointScanUs:F0} ({m_PointScans} scans)"));
            list.Add(Val("Plan cache hits / misses / size", () =>
                $"{m_PlanCache.Hits} / {m_PlanCache.Misses} / {m_PlanCache.Count} of {m_PlanCache.Capacity}"));
            list.Add(Val("Doomed-tree hiding", () => m_HideDoomed ? "on (after-state preview)" : "OFF (fallback rings)"));
            list.Add(Val("Apply status", () => BlockedStatus ?? "-"));
            list.Add(Val("Last plan", () => LastPlan ?? "-"));
            list.Add(Val("Last apply census", () => LastApplyCensus ?? "-"));
            list.Add(Val("Last error", () => LastError == null ? "-" : GladeDebugFirstLine(LastError)));
            list.Add(Val("Species pool", () =>
            {
                string text = "(hook not implemented)";
                DescribeSpeciesPoolForDebug(ref text);
                return text;
            }));

            // Hotkey status: the poller's field-backed StatusLine, resolved once at build time
            SkiGladeHotkeySystem hotkeys = World.GetExistingSystemManaged<SkiGladeHotkeySystem>();
            list.Add(Val("Hotkeys", () => hotkeys == null ? "hotkey system not registered" : hotkeys.StatusLine));
            list.Add(Val("Note", () => "after clicking a button, move the cursor onto the map before applying"));

            list.Add(new DebugUI.Button
            {
                displayName = "Activate glade tool",
                action = () => GladeDebugAction("activate", () =>
                {
                    GameManager gm = GameManager.instance;
                    if (gm == null || !gm.gameMode.IsGame())   // GameModeExtensions.cs:21-24
                    {
                        Mod.Log.Info("Glade: [debug tab] activate ignored — not in a loaded city");
                        return;
                    }
                    bool ok = TryActivate("debug tab");
                    Mod.Log.Info($"Glade: [debug tab] activate {(ok ? "ok" : "refused")}");
                    DebugSystem.Rebuild(BuildSkiGladesDebugUI);
                })
            });
            list.Add(new DebugUI.Button
            {
                displayName = "Deactivate glade tool",
                action = () => GladeDebugAction("deactivate", () =>
                {
                    Deactivate();
                    DebugSystem.Rebuild(BuildSkiGladesDebugUI);
                })
            });
            list.Add(new DebugUI.Button
            {
                displayName = "Toggle target (run interior ⇄ tree line)",
                action = () => GladeDebugAction("toggle target", () => ToggleTarget("debug tab"))
            });
            list.Add(new DebugUI.Button
            {
                displayName = "Next tree density (1 → 8 → 1)",
                action = () => GladeDebugAction("next density", () => StepLevel("debug tab"))
            });
            list.Add(new DebugUI.Button
            {
                displayName = "Toggle doomed-tree hiding (field fallback)",
                action = () => GladeDebugAction("toggle hiding", () =>
                {
                    SetHideDoomedTrees(!HideDoomedTrees, "debug tab");
                })
            });
            list.Add(new DebugUI.Button
            {
                displayName = "Dump diagnostics to mod log",
                action = () => GladeDebugAction("dump", DumpToLog)
            });
            list.Add(new DebugUI.Button
            {
                displayName = "Refresh tab",
                action = () => DebugSystem.Rebuild(BuildSkiGladesDebugUI)   // DebugSystem.cs:971-974
            });

            return list;
        }


        /// <summary>Button bodies run inside the debug UI's invoke path. A throw there must not escape into the menu, and must
        /// not be silent either.</summary>
        private static void GladeDebugAction(string label, Action body)
        {
            try
            {
                body();
            }
            catch (Exception e)
            {
                Mod.Log.Error($"Glade: [debug tab] '{label}' failed:\n{e}");
            }
        }

        /// <summary>A getter that cannot throw into the 10 Hz refresh (copied from ChairPinSystem.Debug.cs:323-334)</summary>
        private static DebugUI.Value Val(string name, Func<object> getter)
        {
            return new DebugUI.Value
            {
                displayName = name,
                getter = () =>
                {
                    try { return getter(); }
                    catch (Exception e) { return "err: " + GladeDebugFirstLine(e.ToString()); }
                },
            };
        }

        private static string GladeDebugEnt(Entity e) => $"{e.Index}:{e.Version}";

        private static string GladeDebugFirstLine(string s)
        {
            int nl = s.IndexOf('\n');
            return nl < 0 ? s : s.Substring(0, nl).TrimEnd('\r');
        }
    }
}
