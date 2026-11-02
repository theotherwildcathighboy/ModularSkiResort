// SkiResortHubUISystem, merged buildings (contract 32; MergeRules header). The merge card beside the game's selected-info panel follows the
// selection like the Ski Bus card: shown for a merged building, or a building that could be merged (or is too small) while the city has a
// ski area. The card's buttons are the triggers mergeBuilding (entity, tier) and unmergeBuilding (entity); SkiMergeSystem does the work. The
// Overview's "Merged buildings" fold reads `mergedLodges`: one row per merged building of the selected area, any state.
// Engine notes: EngineNotes/SkiMergeSystem.md

using System;
using System.Collections.Generic;
using Colossal.Entities;
using Colossal.UI.Binding;
using Game.Common;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiResortHubUISystem
    {
        /// <summary>The merge card refreshes twice a second at 60 fps, and at once after an action.</summary>
        private const int MergeCardRefreshFrames = 30;

        /// <summary>bindings.ts MergeCardState. Tier -1 = not merged. AllowedMask: bit t = tier t fits the lot (MergeRules.AllowedMask).
        /// Refusal / State / Outcome: MergeRules.Refusal, MergeRules.State, SkiMergeSystem.Outcome numbers. Charge[t]: what merging (or
        /// changing) to tier t costs now; Monthly[t]: its monthly fee.</summary>
        public struct HubMergeCardState : IJsonWritable, IEquatable<HubMergeCardState>
        {
            public bool Shown;
            public Entity Building;
            public string Name;
            public string Area;
            public int Tier;
            public int AllowedMask;
            public int Refusal;
            public int State;
            public int Outcome;
            public int[] Charge;
            public int[] Monthly;

            public void Write(IJsonWriter writer)
            {
                writer.TypeBegin("SkiResortMod.MergeCardState");
                writer.PropertyName("shown");
                writer.Write(Shown);
                writer.PropertyName("building");
                UnityWriters.Write(writer, Building);
                writer.PropertyName("name");
                writer.Write(Name ?? "");
                writer.PropertyName("area");
                writer.Write(Area ?? "");
                writer.PropertyName("tier");
                writer.Write(Tier);
                writer.PropertyName("allowedMask");
                writer.Write(AllowedMask);
                writer.PropertyName("refusal");
                writer.Write(Refusal);
                writer.PropertyName("state");
                writer.Write(State);
                writer.PropertyName("outcome");
                writer.Write(Outcome);
                writer.PropertyName("charge");
                WriteInts(writer, Charge);
                writer.PropertyName("monthly");
                WriteInts(writer, Monthly);
                writer.TypeEnd();
            }

            private static void WriteInts(IJsonWriter writer, int[] values)
            {
                int n = values?.Length ?? 0;
                writer.ArrayBegin((uint)n);
                for (int i = 0; i < n; i++)
                    writer.Write(values[i]);
                writer.ArrayEnd();
            }

            public bool Equals(HubMergeCardState other)
            {
                return Shown == other.Shown && Building == other.Building && Tier == other.Tier && AllowedMask == other.AllowedMask &&
                       Refusal == other.Refusal && State == other.State && Outcome == other.Outcome &&
                       string.Equals(Name ?? "", other.Name ?? "", StringComparison.Ordinal) &&
                       string.Equals(Area ?? "", other.Area ?? "", StringComparison.Ordinal) &&
                       SameInts(Charge, other.Charge) && SameInts(Monthly, other.Monthly);
            }

            private static bool SameInts(int[] a, int[] b)
            {
                int n = a?.Length ?? 0;
                if (n != (b?.Length ?? 0))
                    return false;
                for (int i = 0; i < n; i++)
                {
                    if (a[i] != b[i])
                        return false;
                }
                return true;
            }

            public override bool Equals(object obj) => obj is HubMergeCardState other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = Shown ? 1 : 0;
                    h = h * 31 + Building.GetHashCode();
                    h = h * 31 + Tier;
                    h = h * 31 + State;
                    h = h * 31 + Outcome;
                    return h;
                }
            }
        }

        private ValueBinding<HubMergeCardState> m_MergeCard;   // contract 32: always observed; a compare per frame until a building is selected
        private RawValueBinding m_Merged;
        private SkiMergeSystem m_Merge;
        private Entity m_MergeCardSelected, m_MergeCardBuilding;
        private int m_MergeCardTicks;
        private int m_MergedRowsRevision = int.MinValue, m_MergedPushRevision;
        private readonly List<SkiResortAttractivenessSystem.MergedView> m_MergedViewScratch = new List<SkiResortAttractivenessSystem.MergedView>(4);
        private readonly List<MergedRow> m_MergedRows = new List<MergedRow>(4);

        private struct MergedRow
        {
            public Entity Sign;
            public Entity Building;
            public string Name;
            public int Tier;
            public int State;
            public int Monthly;
        }

        private void ResetMergeCard()
        {
            m_MergeCardSelected = m_MergeCardBuilding = Entity.Null;
            m_MergeCardTicks = 0;
            m_MergedRows.Clear();
            m_MergedRowsRevision = int.MinValue;
            m_MergedPushRevision++;
        }

        private void OnMergeBuilding(Entity building, int tier) =>
            Trigger("mergeBuilding", new Request { Kind = RequestKind.MergeBuilding, Entity = building, Int = tier });

        private void OnUnmergeBuilding(Entity building) =>
            Trigger("unmergeBuilding", new Request { Kind = RequestKind.UnmergeBuilding, Entity = building });

        private SkiMergeSystem MergeSystem => m_Merge ?? (m_Merge = World.GetOrCreateSystemManaged<SkiMergeSystem>());

        private void ApplyMerge(Entity building, int tier, string source)
        {
            SkiMergeSystem.Outcome outcome = MergeSystem.Merge(building, tier);
            LastTriggerText = $"mergeBuilding {Ent(building)} tier {tier}: {outcome}{SourceSuffix(source)}";
            m_MergeCardTicks = 0;   // refresh the card on the next frame
        }

        private void ApplyUnmerge(Entity building, string source)
        {
            SkiMergeSystem.Outcome outcome = MergeSystem.Unmerge(building);
            LastTriggerText = $"unmergeBuilding {Ent(building)}: {outcome}{SourceSuffix(source)}";
            m_MergeCardTicks = 0;
        }

        /// <summary>The merge card: follows the game's selection. Hidden (a struct compare) unless the selected building is merged or could be;
        /// refreshed every MergeCardRefreshFrames while one is.</summary>
        private void PushMergeCard()
        {
            Entity selected = m_MergeCard.active ? m_SelectedInfo.selectedEntity : Entity.Null;
            if (selected != m_MergeCardSelected)
            {
                m_MergeCardSelected = selected;
                m_MergeCardBuilding = MergeCardBuildingOf(selected);
                m_MergeCardTicks = 0;
            }
            if (m_MergeCardBuilding == Entity.Null)
            {
                m_MergeCard.Update(default);
                return;
            }
            if (--m_MergeCardTicks > 0)
                return;
            m_MergeCardTicks = MergeCardRefreshFrames;
            m_MergeCard.Update(MergeCardNow());
        }

        /// <summary>The building a selection shows the card for: merged, or a kind that can be merged (a too-small lot included, so the card
        /// says why) while a ski area exists. Null for anything else, our own buildings included.</summary>
        private Entity MergeCardBuildingOf(Entity selected)
        {
            if (selected == Entity.Null || !EntityManager.Exists(selected))
                return Entity.Null;
            if (EntityManager.HasComponent<SkiMergedLodge>(selected))
                return selected;
            if (ResortPresence.Signs <= 0)
                return Entity.Null;
            MergeRules.Facts facts = SkiMergeSystem.ReadFacts(EntityManager, m_PrefabCatalog, selected, forTag: false);
            MergeRules.Kind kind = MergeRules.Classify(facts, out MergeRules.Refusal refusal);
            return kind != MergeRules.Kind.None || refusal == MergeRules.Refusal.TooSmall ? selected : Entity.Null;
        }

        private HubMergeCardState MergeCardNow()
        {
            Entity b = m_MergeCardBuilding;
            if (!EntityManager.Exists(b) || EntityManager.HasComponent<Deleted>(b))
                return default;
            SkiMergeSystem merge = MergeSystem;
            int tiers = SkiCatalog.Lodges.Count;
            var card = new HubMergeCardState
            {
                Shown = true,
                Building = b,
                Name = m_NameSystem.GetRenderedLabelName(b),
                Tier = -1,
                State = (int)MergeRules.State.Unknown,
                Outcome = merge.LastOutcomeBuilding == b ? (int)merge.LastOutcome : 0,
                Charge = new int[tiers],
                Monthly = new int[tiers],
            };
            bool merged = EntityManager.TryGetComponent(b, out SkiMergedLodge tag);
            MergeRules.Facts facts = SkiMergeSystem.ReadFacts(EntityManager, m_PrefabCatalog, b, forTag: merged);
            MergeRules.Classify(facts, out MergeRules.Refusal refusal);
            card.Refusal = (int)refusal;
            card.AllowedMask = MergeRules.AllowedMask(facts.LotCells, tiers);
            for (int t = 0; t < tiers; t++)
            {
                card.Charge[t] = merge.ChargeFor(b, t);
                card.Monthly[t] = merge.MonthlyFeeOf(b, t);
            }
            if (merged)
            {
                card.Tier = tag.m_Tier;
                if (m_Attractiveness.TryGetMergedView(b, out SkiResortAttractivenessSystem.MergedView view))
                {
                    card.State = (int)view.State;
                    card.Area = AreaNameOf(view.Sign);
                }
            }
            return card;
        }

        private string AreaNameOf(Entity sign)
        {
            if (sign == Entity.Null)
                return "";
            for (int i = 0; i < m_Stats.ResortCount; i++)
            {
                ResortStats row = m_Stats.GetResort(i);
                if (row.Sign == sign)
                    return row.Name;
            }
            return "";
        }

        /// <summary>`mergedLodges`: rebuilt when the attraction system's merged views move (or every MergeCardRefreshFrames for names and
        /// fees while the panel shows it), pushed through the usual ArrayPush.</summary>
        private void PushMerged()
        {
            bool active = m_Merged.active;
            if (active)
            {
                int revision = m_Attractiveness.MergedRevision;
                if (revision != m_MergedRowsRevision || m_MergeCardTicks == MergeCardRefreshFrames)
                {
                    m_MergedRowsRevision = revision;
                    RebuildMergedRows();
                }
            }
            PushArray(m_MergedPush, active, m_MergedPushRevision, m_MergedRows.Count);
        }

        private void RebuildMergedRows()
        {
            m_Attractiveness.CopyMergedViews(m_MergedViewScratch);
            bool changed = m_MergedViewScratch.Count != m_MergedRows.Count;
            var rows = new List<MergedRow>(m_MergedViewScratch.Count);
            for (int i = 0; i < m_MergedViewScratch.Count; i++)
            {
                SkiResortAttractivenessSystem.MergedView v = m_MergedViewScratch[i];
                if (!EntityManager.Exists(v.Building))
                    continue;
                rows.Add(new MergedRow
                {
                    Sign = v.Sign,
                    Building = v.Building,
                    Name = m_NameSystem.GetRenderedLabelName(v.Building),
                    Tier = v.Tier,
                    State = (int)v.State,
                    Monthly = MergeRules.Billed(v.State) ? MergeSystem.MonthlyFeeOf(v.Building, v.Tier) : 0,
                });
            }
            rows.Sort((a, c) => string.CompareOrdinal(a.Name ?? "", c.Name ?? ""));
            for (int i = 0; !changed && i < rows.Count; i++)
            {
                MergedRow a = rows[i], c = m_MergedRows[i];
                changed = a.Building != c.Building || a.Sign != c.Sign || a.Tier != c.Tier || a.State != c.State || a.Monthly != c.Monthly ||
                          !string.Equals(a.Name, c.Name, StringComparison.Ordinal);
            }
            if (!changed)
                return;
            m_MergedRows.Clear();
            m_MergedRows.AddRange(rows);
            m_MergedPushRevision++;
        }

        /// <summary>`mergedLodges` (bindings.ts MergedLodgeRow).</summary>
        private void WriteMergedLodges(IJsonWriter writer)
        {
            try
            {
                writer.ArrayBegin((uint)m_MergedRows.Count);
                for (int i = 0; i < m_MergedRows.Count; i++)
                {
                    MergedRow r = m_MergedRows[i];
                    writer.TypeBegin("SkiResortMod.MergedLodgeRow");
                    writer.PropertyName("sign");
                    UnityWriters.Write(writer, r.Sign);
                    writer.PropertyName("building");
                    UnityWriters.Write(writer, r.Building);
                    writer.PropertyName("name");
                    writer.Write(r.Name ?? "");
                    writer.PropertyName("tier");
                    writer.Write(r.Tier);
                    writer.PropertyName("state");
                    writer.Write(r.State);
                    writer.PropertyName("monthly");
                    writer.Write(r.Monthly);
                    writer.TypeEnd();
                }
                writer.ArrayEnd();
            }
            catch (Exception e)
            {
                WriterFailed(writer, "mergedLodges", e);
            }
        }
    }
}
