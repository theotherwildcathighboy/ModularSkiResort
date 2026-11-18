// Contract 48 (T200): the snow report card beside the game's selected-info panel for a selected Welcome Sign, the Ski Bus card's box.
// A section inside the vanilla panel needs moduleRegistry.extend and an internal game-ui path, which check-bundle bans. Always observed
// like busCard: a compare per frame until a sign is selected, then SkiSnowReportSystem.GetReport every SnowCardRefreshFrames. It needs
// no stats Demand (the report reads the catalog rows and the snow map itself).

using System;
using Colossal.UI.Binding;
using Game.Common;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiResortHubUISystem
    {
        private const int SnowCardRefreshFrames = 30;

        /// <summary>bindings.ts SnowCardState.</summary>
        public struct HubSnowCardState : IJsonWritable, IEquatable<HubSnowCardState>
        {
            public bool Shown;
            public SnowReport Report;

            public void Write(IJsonWriter writer)
            {
                writer.TypeBegin("SkiResortMod.SnowCardState");
                writer.PropertyName("shown");
                writer.Write(Shown);
                writer.PropertyName("report");
                Report.Write(writer);
                writer.TypeEnd();
            }

            public bool Equals(HubSnowCardState other) => Shown == other.Shown && Report.Equals(other.Report);
            public override bool Equals(object obj) => obj is HubSnowCardState other && Equals(other);
            public override int GetHashCode() => (Shown ? 1 : 0) ^ Report.GetHashCode();
        }

        private ValueBinding<HubSnowCardState> m_SnowCard;
        private SkiSnowReportSystem m_SnowReport;
        private Entity m_SnowCardSelected, m_SnowCardSign;
        private int m_SnowCardTicks;

        private SkiSnowReportSystem SnowReports => m_SnowReport ?? (m_SnowReport = World.GetOrCreateSystemManaged<SkiSnowReportSystem>());

        /// <summary>The default card state: hidden, every report field written (Gameface one-shape rule).</summary>
        private static HubSnowCardState SnowCardHidden => new HubSnowCardState { Report = SnowReport.Unknown };

        private void ResetSnowCard()
        {
            m_SnowCardSelected = m_SnowCardSign = Entity.Null;
            m_SnowCardTicks = 0;
        }

        private void PushSnowCard()
        {
            Entity selected = m_SnowCard.active ? m_SelectedInfo.selectedEntity : Entity.Null;
            if (selected != m_SnowCardSelected)
            {
                m_SnowCardSelected = selected;
                m_SnowCardSign = selected != Entity.Null && SkiCatalog.IsSign(PrefabNameOf(selected)) ? selected : Entity.Null;
                m_SnowCardTicks = 0;
            }
            if (m_SnowCardSign == Entity.Null || !EntityManager.Exists(m_SnowCardSign) || EntityManager.HasComponent<Deleted>(m_SnowCardSign))
            {
                m_SnowCard.Update(SnowCardHidden);
                return;
            }
            if (--m_SnowCardTicks > 0)
                return;
            m_SnowCardTicks = SnowCardRefreshFrames;
            m_SnowCard.Update(new HubSnowCardState { Shown = true, Report = SnowReports.GetReport(m_SnowCardSign) });
        }
    }
}
