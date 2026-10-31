// Every prefab of this mod is open from the first minute of a city: the mod has no progression of its own. The sign and the buildings
// carry no Unlockable, yet they inherit a lock from ServiceObject.m_Service (Parks & Recreation, a dependency: ServiceObject.cs:42,
// PrefabSystem.cs:137 and :540), and the lines, runs and fence add vanilla requirements on top (Unlockable.cs:43).
// Same route as vanilla's Unlock All without its milestones (UnlockAllSystem.cs:47, SystemOrder.cs:106): one Unlock event per locked
// prefab of ours, UnlockSystem clears the lock and the toolbar refreshes. Vanilla services and prefabs stay as the city has them.
// Once per Game-mode load: a save stores the lock state and a city saved before this system stored it locked.
// SkipUpdate keeps the toolbar's "new" dots off (UIHighlightSystem.cs:145), only when there are events for it to consume.

using System;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.UI.InGame;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiUnlockSystem : GameSystemBase
    {
        private SkiPrefabCatalogSystem m_Catalog;
        private UIHighlightSystem m_UIHighlight;
        private ModificationBarrier1 m_Barrier;
        private EntityArchetype m_UnlockEventArchetype;

        // Per-load state, set at OnGameLoadingComplete
        private bool m_Pending;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Catalog = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>();
            m_UIHighlight = World.GetOrCreateSystemManaged<UIHighlightSystem>();
            m_Barrier = World.GetOrCreateSystemManaged<ModificationBarrier1>();
            m_UnlockEventArchetype = EntityManager.CreateArchetype(ComponentType.ReadWrite<Event>(), ComponentType.ReadWrite<Unlock>());
            Mod.Log.Info($"{nameof(SkiUnlockSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Pending = mode == GameMode.Game;
        }

        protected override void OnUpdate()
        {
            if (!m_Pending)
                return;
            m_Pending = false;
            try
            {
                UnlockOurPrefabs();
            }
            catch (Exception e)
            {
                Mod.Log.Error($"!!! {nameof(SkiUnlockSystem)} EXCEPTION, prefabs of this mod may stay locked until the next load " +
                              "(the hub shows them as not unlocked):\n" + e);
            }
        }

        private void UnlockOurPrefabs()
        {
            int locked = 0, open = 0, missing = 0;
            EntityCommandBuffer buffer = default;
            foreach (CatalogName n in SkiCatalog.AllPrefabNames())
                Unlock(n.TypeName, n.Name, ref buffer, ref locked, ref open, ref missing);
            Unlock(SkiCatalog.TypeLine, DeleteAllRules.ExtraMarkerLV, ref buffer, ref locked, ref open, ref missing);

            if (locked > 0)
                m_UIHighlight.SkipUpdate();
            Mod.Log.Info($"Unlock: {locked} prefab(s) of this mod unlocked, {open} already open, {missing} not in this city");
        }

        private void Unlock(string typeName, string name, ref EntityCommandBuffer buffer, ref int locked, ref int open, ref int missing)
        {
            if (!m_Catalog.TryGetPrefab(typeName, name, out Entity prefab, out PrefabBase _) || prefab == Entity.Null)
            {
                missing++;
                return;
            }
            if (!EntityManager.HasEnabledComponent<Locked>(prefab))
            {
                open++;
                return;
            }
            if (locked == 0)
                buffer = m_Barrier.CreateCommandBuffer();
            Entity e = buffer.CreateEntity(m_UnlockEventArchetype);
            buffer.SetComponent(e, new Unlock(prefab));
            locked++;
        }
    }
}
