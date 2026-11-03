// A re-emitted run's old entity -> its new one (SkiRunReemitSystem, DECISIONS 577), kept by each system with run-keyed runtime maps.
// A pass that started before the swap can still write the old key after it, so the owner re-applies its aliases at the top of every
// update until they age out; Move never overwrites a value already written for the new run. Main thread; no allocation while empty.
// Engine notes: EngineNotes/SkiRunReemitSystem.md

using System;
using System.Collections.Generic;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    internal sealed class RunAliases
    {
        /// <summary>Updates an alias lives: longer than the slowest pass that reads a run list (the score's recompute, the injection's
        /// mask and snow passes), short enough that the list is empty again within a minute at 60 fps.</summary>
        public const int TtlUpdates = 2048;

        private struct Alias
        {
            public Entity From, To;
            public int Ttl;
        }

        private readonly List<Alias> m_List = new List<Alias>(2);

        public bool Any => m_List.Count > 0;

        public void Add(Entity from, Entity to)
        {
            m_List.Add(new Alias { From = from, To = to, Ttl = TtlUpdates });
        }

        public void Clear()
        {
            m_List.Clear();
        }

        /// <summary>Calls <paramref name="move"/> for every alias, then ages them by one update.</summary>
        public void Apply(Action<Entity, Entity> move)
        {
            for (int i = m_List.Count - 1; i >= 0; i--)
            {
                Alias a = m_List[i];
                move(a.From, a.To);
                if (--a.Ttl <= 0)
                    m_List.RemoveAt(i);
                else
                    m_List[i] = a;
            }
        }

        /// <summary>The old key's value moves to the new key unless the new key already has one; the old key is removed either way.</summary>
        public static void Move<T>(Dictionary<Entity, T> map, Entity from, Entity to)
        {
            if (map == null || !map.TryGetValue(from, out T value))
                return;
            map.Remove(from);
            if (!map.ContainsKey(to))
                map[to] = value;
        }
    }
}
