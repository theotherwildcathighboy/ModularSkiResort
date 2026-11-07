// "Ski Grading" tab of the game's developer menu. The pattern is the same as SkiGladeToolSystem.Debug.cs. DebugSystem.RegisterDebug invokes
// each [DebugTab] method of every [DebugContainer] type on the live system (DebugSystem.cs:996-1045), so this partial needs no registration
// in Mod.cs. The tab name must be unique. AddPanel replaces a panel registered under the same name (:1085-1095).
// Value getters read plain fields at 10 Hz. Work runs on a button press only.
// The "Probe: grade a 40 m strip" button writes a 40 m constant ramp with a 1.5 m cut at its middle. The ramp starts 5 m ahead of the
// camera pivot along the view direction. The write goes through the tool's full apply path. 30 frames later, the probe compares CPU heights
// with what it wrote. 120 frames later, it logs the water sim speed.
// "Undo last probe grade" restores the newest probe grade of the session. Built cat tracks are never on the undo stack.

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
                Val("Last solve cost", () => LastCostText),
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
                displayName = "Undo last probe grade (session)",
                action = () => DebugAction("undo", () => UndoLastGrade("dev tab")),
            });
            list.Add(new DebugUI.Button
            {
                displayName = "Open the cat track tool",
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
