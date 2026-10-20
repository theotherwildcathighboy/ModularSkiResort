// CatTrackGradeToolSystem.Debug — the "Ski Grading" tab of the game's developer menu (T139a probe + T139c state; build note
// docs/research/field/2026-10-20/grading-tool-build.md). The SkiGladeToolSystem.Debug.cs pattern: DebugSystem.RegisterDebug sweeps
// every [DebugContainer] type and invokes each [DebugTab] method on the live system (DebugSystem.cs:996-1045), so this file is a
// partial of the tool and nothing is registered in Mod.cs. TAB NAME IS LOAD-BEARING: "Ski Grading" is unique ("Ski Resort",
// "Ski Glades", "Ski Runs", "Ski Resort Hub" are taken; AddPanel replaces a list registered under the same name, :1085-1095).
// COST: value getters read plain fields at 10 Hz; work runs on a button press only.
// THE PROBE (T139a): "Probe: grade a 40 m strip" writes a 40 m constant ramp with a 1.5 m cut at its middle, starting 5 m in front of
// the camera's pivot along the view direction, through the tool's full apply path (read, raster, guards, write verified by readback,
// tail, trees, undo). 30 frames later it compares the CPU heights with what it wrote; 120 frames later it logs the water sim speed.
// Then: save, load, look again. "Undo last grade" restores the newest grade of the session (any, not only the probe).

using System;
using System.Collections.Generic;
using Game;
using Game.Debug;
using Game.SceneFlow;
using UnityEngine.Rendering;

namespace SkiResortMod.Systems
{
    [DebugContainer]
    public sealed partial class CatTrackGradeToolSystem
    {
        [DebugTab("Ski Grading", 4)]
        private List<DebugUI.Widget> BuildSkiGradingDebugUI()
        {
            var list = new List<DebugUI.Widget>
            {
                Val("Armed / disabled / active", () => $"{m_Armed} / {m_Disabled} / {IsActive}"),
                Val("Settings", () => ReadParams().Describe()),
                Val("Points / edits to undo", () => $"{m_Edit.Count} / {m_Edit.UndoDepth}"),
                Val("Last plan", () => LastPlanText),
                Val("Guard", () => m_Guard == GradeGuardKind.None ? "none" : $"{m_Guard} at ({m_GuardAt.x:F0}, {m_GuardAt.y:F0})"),
                Val("Last grade", () => LastApplyText),
                Val("Last heightmap write", () => m_Writer.LastLine),
                Val("Grades to undo (session)", () => $"{m_Undo.Count} ({m_Undo.Bytes / 1048576f:F1} MB)"),
                Val("Probe", () => ProbeText),
                Val("Note", () => "after a button, move the cursor onto the map; the probe grades in front of the camera"),
            };
            list.Add(new DebugUI.Button
            {
                displayName = "Probe: grade a 40 m strip in front of the camera",
                action = () => DebugAction("probe", Probe),
            });
            list.Add(new DebugUI.Button
            {
                displayName = "Undo last grade (session)",
                action = () => DebugAction("undo", () => UndoLastGrade("dev tab")),
            });
            list.Add(new DebugUI.Button
            {
                displayName = "Open the grading tool",
                action = () => DebugAction("activate", () => TryActivate("dev tab")),
            });
            return list;
        }

        private static DebugUI.Value Val(string name, Func<object> getter) => new DebugUI.Value { displayName = name, getter = getter };

        private void DebugAction(string name, Action action)
        {
            try
            {
                GameManager gm = GameManager.instance;
                if (gm == null || !gm.gameMode.IsGame())
                {
                    Mod.Log.Info($"Grade [dev tab]: {name} ignored — not in a loaded city");
                    return;
                }
                Mod.Log.Info($"Grade [dev tab]: {name}");
                action();
                DebugSystem.Rebuild(BuildSkiGradingDebugUI);
            }
            catch (Exception e)
            {
                Fail(e, "dev tab " + name);
            }
        }
    }
}
