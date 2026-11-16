// T226 PROTOTYPE (branch r2/tourism-lab, bridge A/B only; not a shipping design). Adds a relative city Attractiveness modifier of
// Mod.Settings.ResortAttractionDevPct percent, the vanilla airport pattern, so the lab can measure what a resort bonus does to the
// tourist target, the arrivals and the count.
//
// Engine facts (decompile 1.6.4f1):
//   - CityModifierUpdateSystem (GameSimulation, every 256 frames) clears the city's CityModifier buffer and rebuilds it from policies
//     and CityEffectProvider buildings (CityModifierUpdateSystem.cs:119-121, 269-272). A Relative modifier stacks as
//     y' = y (1 + d) + d (:229).
//   - TourismSystem (every 32,768 frames) applies it after the logistic: A += A * y (TourismSystem.cs:104-106, CityUtils.ApplyModifier).
//   - Attractiveness is CityModifierType 0; the buffer may be empty when no building or policy has a modifier (vanilla grows it on demand).
//
// Idempotent re-apply: this system runs every frame after CityModifierUpdateSystem and re-applies only when the buffer no longer holds the
// value it wrote last (vanilla rebuilt it). A changed setting re-applies from the vanilla value it saw. Cost when off: one setting read.

using Game;
using Game.City;
using Game.Simulation;
using Unity.Entities;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class ResortAttractionPrototypeSystem : GameSystemBase
    {
        private CitySystem m_CitySystem;
        private float2 m_Vanilla, m_Written;
        private float m_AppliedPct;
        private bool m_HaveWritten;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_CitySystem = World.GetOrCreateSystemManaged<CitySystem>();
        }

        protected override void OnUpdate()
        {
            float pct = Mod.Settings?.ResortAttractionDevPct ?? 0f;
            Entity city = m_CitySystem.City;
            if (city == Entity.Null || !EntityManager.HasBuffer<CityModifier>(city))
                return;
            if (pct == 0f && !m_HaveWritten)
                return;

            DynamicBuffer<CityModifier> mods = EntityManager.GetBuffer<CityModifier>(city, true);   // read-only until a write is due
            const int type = (int)CityModifierType.Attractiveness;
            float2 now = mods.Length > type ? mods[type].m_Delta : float2.zero;
            bool rebuilt = !m_HaveWritten || math.any(now != m_Written);
            if (!rebuilt && pct == m_AppliedPct)
                return;
            bool vanillaChanged = rebuilt && math.any(now != m_Vanilla);
            if (rebuilt)
                m_Vanilla = now;

            float d = pct / 100f;
            float2 value = m_Vanilla;
            value.y = value.y * (1f + d) + d;   // vanilla Relative stacking, CityModifierUpdateSystem.cs:229
            mods = EntityManager.GetBuffer<CityModifier>(city);
            while (mods.Length <= type)
                mods.Add(default);
            mods[type] = new CityModifier { m_Delta = value };
            if (vanillaChanged || pct != m_AppliedPct)   // not on every 256-frame rebuild: that would be ~1,000 lines a game day
                Mod.Log.Info($"[T226 proto] Attractiveness modifier: vanilla ({m_Vanilla.x:0.####}, {m_Vanilla.y:0.####}) + {pct:0.##} % -> ({value.x:0.####}, {value.y:0.####})");
            m_Written = value;
            m_AppliedPct = pct;
            m_HaveWritten = pct != 0f;
        }
    }
}
