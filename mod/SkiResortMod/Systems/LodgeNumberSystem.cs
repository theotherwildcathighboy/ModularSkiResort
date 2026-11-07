// Numbers same-tier lodges per ski area (LodgeNumberRule). The hub's car space rows say "Chalet 2" while an area has more than one Chalet.
// The number lives in SkiLodgeNumber on the lodge (persisted), so it is the same across frames, saves and loads. Game mode only.
// A pass runs when the attraction snapshot generation moves (a lodge placed, bulldozed or relinked). It also runs every PollFrames frames
// as a backstop, after the first recompute of a load (HasSnapshot). The pass writes only the lodges whose number is missing or belongs to
// another area.
// Phase GameSimulation (not load-bearing: the hub keys its rows on Revision).
// Logs: "created", ARMED once per load, one "LodgeNumber [assign]" per number given, "!!!" once on a throw. DUMP: the shown numbers.
// Engine notes: EngineNotes/LodgeNumberSystem.md

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    public sealed partial class LodgeNumberSystem : GameSystemBase
    {
        internal const int PollFrames = 256;

        private SkiPrefabCatalogSystem m_PrefabCatalog;
        private SkiResortAttractivenessSystem m_Attractiveness;

        private bool m_Armed, m_Disabled, m_ErrorLogged;
        private int m_Frame, m_SigGeneration = int.MinValue, m_Revision, m_Passes, m_Assigned;

        private readonly List<Entity> m_Buildings = new List<Entity>(16);
        private readonly List<Entity> m_Signs = new List<Entity>(16);
        private readonly List<Entity> m_Lodges = new List<Entity>(8);
        private readonly List<LodgeNumberRule.Entry> m_Entries = new List<LodgeNumberRule.Entry>(8);
        private readonly List<LodgeNumberRule.Write> m_Writes = new List<LodgeNumberRule.Write>(2);
        private readonly List<int> m_ShownList = new List<int>(8);
        private readonly Dictionary<Entity, int> m_Shown = new Dictionary<Entity, int>();
        private readonly Dictionary<Entity, int> m_ShownNext = new Dictionary<Entity, int>();

        /// <summary>Moves when any shown number changes (the hub's lodgeParking rows key on it).</summary>
        internal int Revision => m_Revision;

        /// <summary>The number shown after the lodge's tier word. It is 0 while the lodge is the only one of its tier in its area, is not
        /// linked, or has no number yet.</summary>
        internal int ShownNumberOf(Entity lodge)
        {
            return m_Shown.TryGetValue(lodge, out int n) ? n : 0;
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabCatalog = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>();
            m_Attractiveness = World.GetOrCreateSystemManaged<SkiResortAttractivenessSystem>();
            Mod.Log.Info($"{nameof(LodgeNumberSystem)} created (same-tier lodges numbered per ski area, persisted on the lodge)");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Armed = mode == GameMode.Game;
            m_Disabled = m_ErrorLogged = false;
            m_Frame = m_Passes = m_Assigned = 0;
            m_SigGeneration = int.MinValue;
            m_Shown.Clear();
            m_Revision++;
            if (m_Armed)
                Mod.Log.Info("LodgeNumber: ARMED — per ski area and lodge tier, a lodge keeps its number while it exists and stays in that area; " +
                             "a new or relinked lodge takes the lowest free number; the hub shows it only while the area has two or more of that " +
                             $"tier (pass on a new attraction snapshot, backstop every {PollFrames} frames)");
        }

        protected override void OnUpdate()
        {
            if (!m_Armed || m_Disabled)
                return;
            m_Frame++;
            int generation = m_Attractiveness.Generation;
            if (generation == m_SigGeneration && m_Frame % PollFrames != 0)
                return;
            try
            {
                Pass(generation);
            }
            catch (Exception e)
            {
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(LodgeNumberSystem)} EXCEPTION - lodge numbers frozen until the next game load. Please report " +
                                  $"this stack:\n{e}");
                }
            }
        }

        private void Pass(int generation)
        {
            if (!m_PrefabCatalog.IsResolved || !m_Attractiveness.HasSnapshot)
                return;   // no links yet this load
            m_SigGeneration = generation;
            m_Passes++;
            m_Attractiveness.CopyTopLevelLinks(m_Buildings, m_Signs);
            m_Lodges.Clear();
            m_Entries.Clear();
            for (int i = 0; i < m_Buildings.Count; i++)
            {
                Entity b = m_Buildings[i];
                if (!EntityManager.Exists(b) || EntityManager.HasComponent<Deleted>(b) || EntityManager.HasComponent<Temp>(b) ||
                    !EntityManager.TryGetComponent(b, out PrefabRef pr) || !m_PrefabCatalog.LodgeDefs.TryGetValue(pr.m_Prefab, out LodgeDef def))
                    continue;
                bool has = EntityManager.TryGetComponent(b, out SkiLodgeNumber held);
                m_Lodges.Add(b);
                m_Entries.Add(new LodgeNumberRule.Entry
                {
                    Lodge = LiftCensusSystem.Ref(b),
                    Sign = m_Signs[i] == Entity.Null ? EntityRef.Null : LiftCensusSystem.Ref(m_Signs[i]),
                    Tier = def.Index,
                    Has = has,
                    HeldSign = has && held.m_Sign != Entity.Null ? LiftCensusSystem.Ref(held.m_Sign) : EntityRef.Null,
                    Number = has ? held.m_Number : 0,
                });
            }
            LodgeNumberRule.Assign(m_Entries, m_Writes, m_ShownList);

            for (int w = 0; w < m_Writes.Count; w++)
            {
                LodgeNumberRule.Write write = m_Writes[w];
                Entity lodge = LiftCensusSystem.Ent(write.Lodge);
                Entity sign = LiftCensusSystem.Ent(write.Sign);
                bool has = EntityManager.TryGetComponent(lodge, out SkiLodgeNumber old);
                var value = new SkiLodgeNumber(sign, write.Number);
                if (has)
                    EntityManager.SetComponentData(lodge, value);
                else
                    EntityManager.AddComponentData(lodge, value);
                m_Assigned++;
                Mod.Log.Info($"LodgeNumber [assign] {NameOf(lodge)} {Id(lodge)}: number {write.Number} in the area of Welcome Sign {Id(sign)} (" +
                             (!has ? "had none" : old.m_Sign != sign ? $"had {old.m_Number} in another area" : $"had {old.m_Number}, held by a lower entity") + ")");
            }

            m_ShownNext.Clear();
            for (int i = 0; i < m_Lodges.Count && i < m_ShownList.Count; i++)
            {
                if (m_ShownList[i] > 0)
                    m_ShownNext[m_Lodges[i]] = m_ShownList[i];
            }
            if (!SameShown())
            {
                m_Shown.Clear();
                foreach (KeyValuePair<Entity, int> kv in m_ShownNext)
                    m_Shown[kv.Key] = kv.Value;
                m_Revision++;
            }
        }

        private bool SameShown()
        {
            if (m_Shown.Count != m_ShownNext.Count)
                return false;
            foreach (KeyValuePair<Entity, int> kv in m_ShownNext)
            {
                if (!m_Shown.TryGetValue(kv.Key, out int n) || n != kv.Value)
                    return false;
            }
            return true;
        }

        private string NameOf(Entity lodge)
        {
            return EntityManager.TryGetComponent(lodge, out PrefabRef pr) && m_PrefabCatalog.LodgeDefs.TryGetValue(pr.m_Prefab, out LodgeDef def)
                ? def.Id
                : "lodge";
        }

        /// <summary>DUMP: the counts and every shown number.</summary>
        internal void AppendDump(StringBuilder sb)
        {
            sb.Append("  lodge numbers: armed ").Append(m_Armed).Append(", disabled ").Append(m_Disabled).Append(", passes ").Append(m_Passes)
              .Append(", assigned ").Append(m_Assigned).Append(';');
            if (m_Shown.Count == 0)
                sb.Append(" no area has two lodges of one tier");
            foreach (KeyValuePair<Entity, int> kv in m_Shown)
                sb.Append(' ').Append(NameOf(kv.Key)).Append(' ').Append(Id(kv.Key)).Append(" = ").Append(kv.Value).Append(';');
            sb.Append('\n');
        }

        private static string Id(Entity e) => e.Index.ToString(CultureInfo.InvariantCulture) + ":" + e.Version.ToString(CultureInfo.InvariantCulture);
    }
}
