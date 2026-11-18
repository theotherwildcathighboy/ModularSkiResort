// T226 resort city tourism bonus (DECISIONS 666). The rules are TourismBonusRules (pure, ResortBatch TB-*). This system:
//   1. reads each ski area's last score (SkiResortAttractivenessSystem: Result and active lodges) when that system's Generation or the
//      "Resort tourism bonus" option changes, and sums the areas into the city bonus (capped by the option, 0 = off);
//   2. writes it into the city's CityModifier buffer as ONE more Relative Attractiveness modifier (the vanilla airport pattern);
//   3. samples the city's tourist count for the hub's day mean (TouristDayMean).
//
// Engine facts (decompile 1.6.0f1 / 1.6.2f1, unchanged between them):
//   - CityModifierUpdateSystem (GameSimulation, interval 256) clears the city's CityModifier buffer and rebuilds it from policies and
//     CityEffectProvider buildings (CityModifierUpdateSystem.cs:119-121, 269-272). Relative modifiers stack y' = y (1 + d) + d (:229).
//   - TourismSystem (every 32,768 frames) applies it after the logistic: A += A * y (TourismSystem.cs:104-106). Tourism Overhaul reads
//     that A (tourism-lab REPORT 2.1).
//   - CityModifierType.Attractiveness is 0. The buffer may be shorter than that when nothing has a modifier: we grow it as vanilla does.
//
// Idempotent re-apply: this system runs every simulation frame right after CityModifierUpdateSystem and writes only when the buffer no
// longer holds the value it wrote last (vanilla rebuilt it) or the bonus changed. A rebuild is followed within the same frame, so
// TourismSystem never reads the buffer without the bonus (bridge A/B: 8 game days, no drift, tourism-lab 2.3).
// Nothing is saved: the buffer is serialized with the city, but vanilla rebuilds it 256 frames after a load, and this system writes
// nothing for the first SettleFrames after a load (so a save's stacked value is never stacked again). An uninstall is clean.
// Cost while off (option 0, or no ski area): one setting read and a frame compare. The tourist sample is every 4,096 frames.
// Log: one line per load with the option, then a line only when the applied bonus moved TourismBonusRules.LogStepPct or turned on / off.

using System;
using System.Collections.Generic;
using System.Globalization;
using Colossal.Serialization.Entities;
using Game;
using Game.City;
using Game.Simulation;
using SkiResortMod.Settings;
using Unity.Entities;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class ResortTourismBonusSystem : GameSystemBase
    {
        /// <summary>Frames after a load before the first write: vanilla's first rebuild (interval 256) has replaced the saved buffer by then.</summary>
        private const uint SettleFrames = 512;

        /// <summary>One ski area's part of the last sum (the hub's tip).</summary>
        internal struct AreaView
        {
            public Entity Sign;
            public int LiftLines, TierPoints;
            public float SizePct, BonusPct;
            public int OpenSharePct;
        }

        private CitySystem m_CitySystem;
        private SimulationSystem m_Simulation;
        private SkiResortAttractivenessSystem m_Attract;
        private CountHouseholdDataSystem m_Households;

        private readonly List<Entity> m_Signs = new List<Entity>(4);
        private readonly List<SkiResortAttractivenessSystem.LodgeInfo> m_Lodges = new List<SkiResortAttractivenessSystem.LodgeInfo>(8);
        private readonly Dictionary<Entity, AreaView> m_Areas = new Dictionary<Entity, AreaView>();
        private readonly TouristDayMean m_DayMean = new TouristDayMean();

        private bool m_InGame;
        private uint m_LoadFrame, m_LastSample;
        private int m_LastGeneration = int.MinValue, m_CapPct = -1;
        private double m_SumPct, m_TargetPct, m_LoggedPct;
        private bool m_LoggedOnce;

        // The write: the vanilla value seen at the last rebuild, the value written, and the bonus it carried.
        private float2 m_Vanilla, m_Written;
        private double m_AppliedPct;
        private bool m_HaveWritten;

        /// <summary>The bonus in the city's modifier now, percent (0 = none).</summary>
        internal float AppliedPct => m_HaveWritten ? (float)m_AppliedPct : 0f;

        /// <summary>The option's cap, percent (0 = off).</summary>
        internal int CapPct => m_CapPct < 0 ? SkiResortSettings.CapPctOf(Mod.Settings) : m_CapPct;

        /// <summary>The areas' sum before the cap, percent.</summary>
        internal float SumPct => (float)m_SumPct;

        /// <summary>The city's tourist count averaged over the last game day (-1 until 3 game hours are sampled after a load).</summary>
        internal int TouristsDayMean => m_DayMean.Mean;

        internal bool TryGetArea(Entity sign, out AreaView view) => m_Areas.TryGetValue(sign, out view);

        protected override void OnCreate()
        {
            base.OnCreate();
            m_CitySystem = World.GetOrCreateSystemManaged<CitySystem>();
            m_Simulation = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_Attract = World.GetOrCreateSystemManaged<SkiResortAttractivenessSystem>();
            m_Households = World.GetOrCreateSystemManaged<CountHouseholdDataSystem>();
            Mod.Log.Info($"{nameof(ResortTourismBonusSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_InGame = mode.IsGame();
            m_LoadFrame = m_LastSample = m_Simulation.frameIndex;
            m_LastGeneration = int.MinValue;
            m_CapPct = -1;
            m_SumPct = m_TargetPct = m_LoggedPct = 0.0;
            m_LoggedOnce = false;
            m_Vanilla = m_Written = float2.zero;
            m_AppliedPct = 0.0;
            m_HaveWritten = false;
            m_Areas.Clear();
            m_DayMean.Clear();
        }

        protected override void OnUpdate()
        {
            if (!m_InGame)
                return;
            Entity city = m_CitySystem.City;
            if (city == Entity.Null)
                return;
            uint frame = m_Simulation.frameIndex;
            if (frame - m_LoadFrame < SettleFrames)
                return;

            if (frame - m_LastSample >= (uint)TouristDayMean.SampleFrames)
            {
                m_LastSample = frame;
                m_DayMean.Add(m_Households.TouristCitizenCount);
            }

            int cap = SkiResortSettings.CapPctOf(Mod.Settings);
            if (cap != m_CapPct || m_Attract.Generation != m_LastGeneration)
                Recompute(cap);

            if (m_TargetPct <= 0.0 && !m_HaveWritten)
                return;
            if (!EntityManager.HasBuffer<CityModifier>(city))
                return;
            Apply(city);
        }

        /// <summary>The areas' parts and the city target from the attractiveness system's last recompute.</summary>
        private void Recompute(int cap)
        {
            bool capChanged = cap != m_CapPct;
            m_CapPct = cap;
            m_LastGeneration = m_Attract.Generation;
            m_Areas.Clear();
            double sum = 0.0;
            m_Attract.CopySigns(m_Signs);
            for (int s = 0; s < m_Signs.Count; s++)
            {
                Entity sign = m_Signs[s];
                if (!m_Attract.TryGetAreaScore(sign, out SkiResortAttractivenessSystem.AreaScoreView v))
                    continue;
                SkiResortScore.Result r = v.Result;
                int points = 0;
                m_Attract.CopyActiveLodgeInfo(sign, m_Lodges);
                for (int i = 0; i < m_Lodges.Count; i++)
                {
                    int at = m_Lodges[i].LodgeIndex;
                    string id = at >= 0 && at < SkiCatalog.Lodges.Count ? SkiCatalog.Lodges[at].Id : null;
                    points += TourismBonusRules.TierPointsOf(id, m_Lodges[i].Merged);
                }
                double size = TourismBonusRules.AreaSizePct(r.LiftsCounted, points);
                double share = TourismBonusRules.OpenShare(r.RunsValue, r.LiftsApplied, r.RunsValueFull, r.LiftsValue);
                // A sign with no efficiency (destroyed, disabled) scores 0, and so gives no bonus.
                double bonus = r.Efficiency > 0.0 ? TourismBonusRules.AreaBonusPct(size, share) : 0.0;
                sum += bonus;
                m_Areas[sign] = new AreaView
                {
                    Sign = sign, LiftLines = r.LiftsCounted, TierPoints = points, SizePct = (float)size, BonusPct = (float)bonus,
                    OpenSharePct = (int)Math.Round(100.0 * share, MidpointRounding.AwayFromZero),
                };
            }
            m_SumPct = sum;
            m_TargetPct = TourismBonusRules.CityBonusPct(sum, cap);

            if (!m_LoggedOnce || capChanged || TourismBonusRules.LogWorthy(m_LoggedPct, m_TargetPct))
            {
                Mod.Log.Info(string.Format(CultureInfo.InvariantCulture,
                    "Resort tourism bonus: {0:0.##} % city-wide (areas {1}, sum {2:0.##} %, option cap {3} %{4})",
                    m_TargetPct, m_Areas.Count, sum, cap, cap == 0 ? ", off" : ""));
                m_LoggedPct = m_TargetPct;
                m_LoggedOnce = true;
            }
        }

        /// <summary>The write (see the header). Main thread.</summary>
        private void Apply(Entity city)
        {
            const int type = (int)CityModifierType.Attractiveness;
            DynamicBuffer<CityModifier> mods = EntityManager.GetBuffer<CityModifier>(city, true);   // read-only until a write is due
            float2 now = mods.Length > type ? mods[type].m_Delta : float2.zero;
            bool rebuilt = !m_HaveWritten || math.any(now != m_Written);
            if (!rebuilt && m_TargetPct == m_AppliedPct)
                return;
            if (rebuilt)
                m_Vanilla = now;

            float2 value = m_Vanilla;
            if (m_TargetPct > 0.0)
                value.y = TourismBonusRules.StackRelative(m_Vanilla.y, (float)(m_TargetPct / 100.0));
            mods = EntityManager.GetBuffer<CityModifier>(city);
            while (mods.Length <= type)
                mods.Add(default);
            mods[type] = new CityModifier { m_Delta = value };
            m_Written = value;
            m_AppliedPct = m_TargetPct;
            m_HaveWritten = m_TargetPct > 0.0;
        }
    }
}
