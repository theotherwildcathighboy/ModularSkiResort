// The warning before a sign is bulldozed: the vanilla prompt says nothing about the runs this system deletes with it. The bulldozer
// asks through a public delegate, so we stand in front of it: a sign with bound runs gets our dialog, anything else the vanilla chain.
// Any failure falls back to the vanilla prompt: the tool waits in State.Waiting until someone answers (BulldozeToolSystem.cs:1567-1573).
// Engine notes: EngineNotes/SkiRunGateCascadeSystem.md

using System;
using System.Collections.Generic;
using Game.Buildings;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Tools;
using Game.UI;
using Game.UI.Localization;
using Unity.Collections;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiRunGateCascadeSystem
    {
        public const string KeyConfirmOneRun = "SkiResort.SignDelete.CONFIRM_ONE";
        public const string KeyConfirmRuns = "SkiResort.SignDelete.CONFIRM_MANY";

        private BulldozeToolSystem m_Bulldozer;
        private EntityQuery m_TempBuildings;
        private Action m_VanillaConfirm;

        // Scratch, reused every use
        private readonly HashSet<Entity> m_SignsInPreview = new HashSet<Entity>();

        private void CreateConfirm()
        {
            m_Bulldozer = World.GetOrCreateSystemManaged<BulldozeToolSystem>();
            m_TempBuildings = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Building>(),
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Game.Common.Deleted>(),
                },
            });
        }

        /// <summary>Once per world: ToolUISystem subscribed at its OnCreate (ToolUISystem.cs:123-124), so by a game load the chain holds it.
        /// An empty chain stays alone (the bulldozer would not prompt at all) and is tried again at the next load.</summary>
        private void WrapBulldozeConfirm()
        {
            if (m_VanillaConfirm != null)
                return;
            Action chain = m_Bulldozer.EventConfirmationRequested;
            if (chain == null)
            {
                Mod.Log.Warn("SignConfirm: the bulldozer has no confirmation subscriber, so a sign is deleted without our warning");
                return;
            }
            m_VanillaConfirm = chain;
            m_Bulldozer.EventConfirmationRequested = OnBulldozeConfirmRequested;
            Mod.Log.Info("SignConfirm: in front of the bulldozer's confirmation (a sign with bound runs gets the run-count dialog)");
        }

        private void OnBulldozeConfirmRequested()
        {
            int runs = 0;
            try
            {
                // Disabled cascade: the runs stay as orphans, so "deletes" would be false
                if (m_Armed && !m_Disabled)
                    runs = RunsInBulldozerPreview();
            }
            catch (Exception e)
            {
                runs = 0;
                Mod.Log.Error("!!! SignConfirm EXCEPTION, the vanilla prompt asks instead:\n" + e);
            }

            if (runs == 0)
            {
                m_VanillaConfirm();
                return;
            }

            Mod.Log.Info($"SignConfirm: {m_SignsInPreview.Count} sign(s) in the bulldozer, {runs} bound run(s) at stake: own dialog");
            LocalizedString message = runs == 1
                ? LocalizedString.Id(KeyConfirmOneRun)
                : LocalizedString.Id(KeyConfirmRuns, ("COUNT", new LocalizedNumber<int>(runs, "integer")));
            // Same dialog shape and answer as ToolUISystem.cs:447-455, with vanilla's Warning title
            GameManager.instance.userInterface.appBindings.ShowConfirmationDialog(
                new ConfirmationDialog("Common.DIALOG_TITLE[Warning]", message, "Common.DIALOG_ACTION[Yes]", "Common.DIALOG_ACTION[No]"),
                OnSignConfirmAnswer);
        }

        private void OnSignConfirmAnswer(int msg)
        {
            Mod.Log.Info($"SignConfirm: answered {(msg == 0 ? "yes, deleting" : "no, kept")}");
            m_Bulldozer.ConfirmAction(msg == 0);
        }

        /// <summary>Runs bound to the signs the bulldozer's preview would delete: its Temp copies carry TempFlags.Delete and the sign itself
        /// in m_Original (GenerateObjectsSystem.cs:989-1009), the flag ConfirmationNeeded reads (BulldozeToolSystem.cs:1594-1608).</summary>
        private int RunsInBulldozerPreview()
        {
            m_SignsInPreview.Clear();
            using (var temps = m_TempBuildings.ToComponentDataArray<Temp>(Allocator.Temp))
            using (var prefabRefs = m_TempBuildings.ToComponentDataArray<PrefabRef>(Allocator.Temp))
            {
                int n = Math.Min(temps.Length, prefabRefs.Length);
                for (int i = 0; i < n; i++)
                {
                    if ((temps[i].m_Flags & TempFlags.Delete) != 0 && m_Ownership.IsGatePrefab(prefabRefs[i].m_Prefab))
                        m_SignsInPreview.Add(temps[i].m_Original);
                }
            }
            if (m_SignsInPreview.Count == 0)
                return 0;

            int runs = 0;
            using (var bindings = m_BoundRuns.ToComponentDataArray<SkiRunBinding>(Allocator.Temp))
            {
                for (int i = 0; i < bindings.Length; i++)
                {
                    if (m_SignsInPreview.Contains(bindings[i].m_Gate))
                        runs++;
                }
            }
            return runs;
        }
    }
}
