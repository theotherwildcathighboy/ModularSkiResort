// The hub row's cat track delete, applied where vanilla sees it. The hub runs in UIUpdate, after ModificationSystem in MainLoop
// (Game.Common/SystemOrder.cs:58-67). CleanUpSystem destroys Deleted entities right after MainLoop
// (Game.SceneFlow/GameManager.cs:2391-2399). A Deleted tag added from the hub would be gone before Game.Areas.SearchSystem (Modification5,
// SystemOrder.cs:200) reads it. The destroyed area stays in the search tree, and a later Burst job crashes on it (field test 2026-10-31,
// FIX-LIST 0).
// Scheduling (load-bearing): UpdateBefore<SkiCatTrackDeleteSystem, SubElementDeleteSystem>(PostTool). This is the Delete all anchor.
// PostTool runs every frame, paused included (Game.Tools/ToolSystem.cs:251-257). Gate: tools/check-ui-phase-deletes.js.

using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Game;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiCatTrackDeleteSystem : GameSystemBase
    {
        private readonly List<KeyValuePair<Entity, string>> m_Queue = new List<KeyValuePair<Entity, string>>();
        private CatTrackGradeToolSystem m_GradeTool;

        /// <summary>Queues one delete for this frame's or the next frame's PostTool.</summary>
        public void Request(Entity run, string why) => m_Queue.Add(new KeyValuePair<Entity, string>(run, why));

        protected override void OnCreate()
        {
            base.OnCreate();
            m_GradeTool = World.GetOrCreateSystemManaged<CatTrackGradeToolSystem>();
            Mod.Log.Info($"{nameof(SkiCatTrackDeleteSystem)} created");
        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            // A request never crosses a load
            m_Queue.Clear();
        }

        protected override void OnUpdate()
        {
            if (m_Queue.Count == 0)
                return;
            foreach (KeyValuePair<Entity, string> request in m_Queue)
                m_GradeTool.DeleteTrack(request.Key, request.Value);
            m_Queue.Clear();
        }
    }
}
