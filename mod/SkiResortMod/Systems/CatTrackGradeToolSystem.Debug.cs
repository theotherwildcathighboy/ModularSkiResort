// "Ski Grading" tab of the game's developer menu. The pattern is the same as SkiGladeToolSystem.Debug.cs. DebugSystem.RegisterDebug invokes
// each [DebugTab] method of every [DebugContainer] type on the live system (DebugSystem.cs:996-1045), so this partial needs no registration
// in Mod.cs. The tab name must be unique. AddPanel replaces a panel registered under the same name (:1085-1095).
// Value getters read plain fields at 10 Hz. The one button opens the tool. The dev probe and its undo stack were removed in T194 (planners-12):
// a built cat track is never un-graded, and the probe was their only user.

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
                Val("Ski way", () => m_SkiWay ? "on: " + CurrentParams().Describe() : "off"),
                Val("Points / edits to undo", () => $"{m_Edit.Count} / {m_Edit.UndoDepth}"),
                Val("Last plan", () => LastPlanText),
                Val("Last solve cost", () => LastCostText),
                Val("Guard", () => m_Guard == GradeGuardKind.None ? "none" : $"{m_Guard} at ({m_GuardAt.x:F0}, {m_GuardAt.y:F0})"),
                Val("Last grade", () => LastApplyText),
                Val("Last heightmap write", () => m_Writer.LastLine),
            };
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
