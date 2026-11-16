// SkiResortHubUISystem, Sync Resort (contract 51, T229, DECISIONS 670). The Overview shows "Sync resort (N)" for the shown area while N > 0
// (N = objects of the area that an Offer step lists, not yet synced; failed ones count again). The button opens the Sync card in the
// Overview: one row per listed object with its step's sentence, a check box (unchecked = skip it this time), "Keep as is" and "Fly to", and
// one Sync button for the checked rows. Sync is free and has no undo. Objects edited by hand show for a step that skips them, unchecked and
// never synced. While the card is open, SkiSyncOverlaySystem draws a ring around each listed object of the area.
// Binding `sync` (bindings.ts SyncState): ready, areas [{sign, n}], open, sign, rows (only for the open card's area), running, done,
// failed, total. Triggers: syncCard (sign, open), syncInclude (entity, on), syncKeep (entity), syncRun (sign). "Fly to" is focusBuilding.
// SkiSyncSystem does the work and owns the list; this file only shows it. The card closes when the hub closes.
// Engine notes: EngineNotes/SkiSyncSystem.md

using System.Collections.Generic;
using Colossal.UI.Binding;
using Game.Common;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiResortHubUISystem
    {
        private SkiSyncSystem m_SyncSystem;
        private RawValueBinding m_SyncBinding;
        private int m_SyncSeen = int.MinValue;
        private bool m_SyncWasActive;
        private readonly Dictionary<Entity, int> m_SyncAreaCounts = new Dictionary<Entity, int>();
        private readonly List<Entity> m_SyncAreaOrder = new List<Entity>();

        private SkiSyncSystem SyncSystem => m_SyncSystem ?? (m_SyncSystem = World.GetOrCreateSystemManaged<SkiSyncSystem>());

        private void OnSyncCard(Entity sign, bool open) =>
            Trigger("syncCard", new Request { Kind = RequestKind.SyncCard, Entity = sign, Bool = open });

        private void OnSyncInclude(Entity entity, bool on) =>
            Trigger("syncInclude", new Request { Kind = RequestKind.SyncInclude, Entity = entity, Bool = on });

        private void OnSyncKeep(Entity entity) =>
            Trigger("syncKeep", new Request { Kind = RequestKind.SyncKeep, Entity = entity });

        private void OnSyncRun(Entity sign) =>
            Trigger("syncRun", new Request { Kind = RequestKind.SyncRun, Entity = sign });

        private void ApplySync(Request r)
        {
            SkiSyncSystem sync = SyncSystem;
            switch (r.Kind)
            {
                case RequestKind.SyncCard:
                    sync.SetCard(r.Bool, r.Entity);
                    LastTriggerText = $"syncCard {Ent(r.Entity)} {(r.Bool ? "open" : "closed")}{SourceSuffix(r.Source)}";
                    Mod.Log.Info($"Hub [sync] card {(r.Bool ? "open" : "closed")} for {(r.Entity == Entity.Null ? "Not linked" : Ent(r.Entity))} " +
                                 $"({CountFor(sync, r.Entity)} listed){SourceSuffix(r.Source)}");
                    break;
                case RequestKind.SyncInclude:
                    sync.SetInclude(r.Entity, r.Bool);
                    LastTriggerText = $"syncInclude {Ent(r.Entity)} {r.Bool}";
                    break;
                case RequestKind.SyncKeep:
                    LastTriggerText = $"syncKeep {Ent(r.Entity)}: {(sync.Keep(r.Entity) ? "kept" : "refused")}";
                    break;
                case RequestKind.SyncRun:
                    Entity sign = r.Entity;
                    int n = sync.RequestSync(e => sync.AreaOf(e) == sign);
                    LastTriggerText = $"syncRun {Ent(sign)}: {n} queued";
                    Mod.Log.Info($"Hub [sync] Sync for {(sign == Entity.Null ? "Not linked" : Ent(sign))}: {n} object(s) queued{SourceSuffix(r.Source)}");
                    break;
            }
        }

        private static int CountFor(SkiSyncSystem sync, Entity sign)
        {
            int n = 0;
            IReadOnlyList<SkiSyncSystem.Item> items = sync.Items;
            for (int i = 0; i < items.Count; i++)
                if (Counts(items[i]) && sync.AreaOf(items[i].Entity) == sign)
                    n++;
            return n;
        }

        /// <summary>An item counts for "Sync resort (N)" while Sync can still act on it.</summary>
        private static bool Counts(SkiSyncSystem.Item it) =>
            it.State != SkiSyncSystem.ItemState.HandEdited;

        /// <summary>The card closes with the hub (the world markers go with it).</summary>
        private void CloseSyncCardWithHub()
        {
            if (m_SyncSystem != null && m_SyncSystem.CardOpen)
            {
                m_SyncSystem.SetCard(false, Entity.Null);
                Mod.Log.Info("Hub [sync] card closed with the hub");
            }
        }

        /// <summary>`sync`: pushes when the engine's list moved or the binding was just subscribed. Panel only.</summary>
        private void PushSync()
        {
            bool active = m_SyncBinding.active;
            if (!active)
            {
                m_SyncWasActive = false;
                return;
            }
            int rev = SyncSystem.Revision;
            if (m_SyncWasActive && rev == m_SyncSeen)
                return;
            m_SyncWasActive = true;
            m_SyncSeen = rev;
            m_SyncBinding.Update();
        }

        /// <summary>bindings.ts SyncState (one shape: every field, every time).</summary>
        private void WriteSync(IJsonWriter writer)
        {
            SkiSyncSystem sync = SyncSystem;
            IReadOnlyList<SkiSyncSystem.Item> items = sync.Items;
            m_SyncAreaCounts.Clear();
            m_SyncAreaOrder.Clear();
            for (int i = 0; i < items.Count; i++)
            {
                if (!Counts(items[i]) || !EntityManager.Exists(items[i].Entity) || EntityManager.HasComponent<Deleted>(items[i].Entity))
                    continue;
                Entity a = sync.AreaOf(items[i].Entity);
                if (!m_SyncAreaCounts.TryGetValue(a, out int n))
                    m_SyncAreaOrder.Add(a);
                m_SyncAreaCounts[a] = n + 1;
            }
            writer.TypeBegin("SkiResortMod.SyncState");
            writer.PropertyName("ready");
            writer.Write(sync.IntakeDone);
            writer.PropertyName("areas");
            writer.ArrayBegin(m_SyncAreaOrder.Count);
            foreach (Entity a in m_SyncAreaOrder)
            {
                writer.TypeBegin("SkiResortMod.SyncArea");
                writer.PropertyName("sign");
                UnityWriters.Write(writer, a);
                writer.PropertyName("n");
                writer.Write(m_SyncAreaCounts[a]);
                writer.TypeEnd();
            }
            writer.ArrayEnd();
            bool open = sync.CardOpen;
            Entity sign = sync.CardSign;
            writer.PropertyName("open");
            writer.Write(open);
            writer.PropertyName("sign");
            UnityWriters.Write(writer, sign);
            int rows = 0;
            if (open)
            {
                for (int i = 0; i < items.Count; i++)
                    if (ShownRow(sync, items[i], sign))
                        rows++;
            }
            writer.PropertyName("rows");
            writer.ArrayBegin(rows);
            if (open)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    SkiSyncSystem.Item it = items[i];
                    if (!ShownRow(sync, it, sign))
                        continue;
                    writer.TypeBegin("SkiResortMod.SyncRow");
                    writer.PropertyName("entity");
                    UnityWriters.Write(writer, it.Entity);
                    writer.PropertyName("kind");
                    writer.Write((int)it.Kind);
                    writer.PropertyName("step");
                    writer.Write(it.Step);
                    writer.PropertyName("name");
                    writer.Write(MergedBuildingName(it.Entity) ?? "");
                    writer.PropertyName("include");
                    writer.Write(it.Include);
                    writer.PropertyName("state");
                    writer.Write((int)it.State);
                    writer.TypeEnd();
                }
            }
            writer.ArrayEnd();
            writer.PropertyName("running");
            writer.Write(sync.Running);
            writer.PropertyName("done");
            writer.Write(sync.RunDone);
            writer.PropertyName("failed");
            writer.Write(sync.RunFailed);
            writer.PropertyName("total");
            writer.Write(sync.RunTotal);
            writer.TypeEnd();
        }

        private bool ShownRow(SkiSyncSystem sync, SkiSyncSystem.Item it, Entity sign) =>
            EntityManager.Exists(it.Entity) && !EntityManager.HasComponent<Deleted>(it.Entity) && sync.AreaOf(it.Entity) == sign;
    }
}
