// Makes Night-gated lights on our run props follow the day/night cycle.
// Why: nothing in the engine re-evaluates EffectConditionFlags.Night on a static prop. A light loaded at night therefore stays ON next day
// until a tool hover. This system stamps the re-evaluation tags at each flip (our StreetLightSystem).
// Registration is load-bearing: UpdateAfter<EffectFlagSystem>(GameSimulation). GetUpdateInterval must equal EffectFlagSystem's
// 2048 (UpdateSystem inherits an anchor's runtime offset only for EQUAL intervals, UpdateSystem.cs:406/:431).
// Scope: only the props the placer tagged with SkiRunPropMember. A city with no resort or no such prop returns before the system reads any query result.
// A mast's aim is set once, at placement (RunPropPlacerRules, MastAimRules): nothing rewrites a free-standing prop's rotation.
// Engine notes: EngineNotes/SkiNightLightSystem.md

using System;
using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Effects;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiNightLightSystem : GameSystemBase
    {
        private static class Tuning
        {
            /// <summary>Passes to keep re-stamping after a day/night flip: 8 x 2048 = 16384 sim frames, covering EffectFlagSystem's
            /// 15000-tick stagger should a patch revive it.</summary>
            public const int SettlePasses = 8;
        }

        // Must equal EffectFlagSystem.GetUpdateInterval (2048) or the anchored offset is not inherited
        private const int FlagInterval = 2048;

        private EffectFlagSystem m_EffectFlagSystem;
        private EntityQuery m_MemberQuery;

        private bool m_Armed;
        private bool m_Disabled;
        private bool m_ErrorLogged;
        private bool m_HaveFlag;
        private bool m_LastNight;
        private int m_SettleRemaining;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_EffectFlagSystem = World.GetOrCreateSystemManaged<EffectFlagSystem>();

            m_MemberQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<SkiRunPropMember>() },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });

            Mod.Log.Info($"{nameof(SkiNightLightSystem)} created");
        }

        /// <summary>Must match EffectFlagSystem's 2048 (see engine notes).</summary>
        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return FlagInterval;
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            m_Armed = false;
            m_Disabled = false;
            m_ErrorLogged = false;
            m_HaveFlag = false;
            m_SettleRemaining = 0;

            if (mode != GameMode.Game)
                return;

            try
            {
                // One line per load for the three run-visual features
                Mod.Log.Info($"RunVisuals: ARMED - (T22) masts are placed facing into the run with up to " +
                             $"{MastAimRules.BiasMaxDeg:0} deg downhill lean (lamp pitch stays the prefab's 25 deg); (T30) glade " +
                             $"tool outlines {HexOf(RunOutlineRules.GladeOutlineColour)} {RunOutlineRules.GladeLineWidthM:0.0#} m, " +
                             $"dashed {RunOutlineRules.GladeDashM:0}/{RunOutlineRules.GladeGapM:0} m on bound runs, solid when " +
                             $"hovered; (T36) run corner dots opaque white with a dark rim at Options 'run outline size' (0 = the " +
                             "game's own dots)");
                m_Armed = true;
                Mod.Log.Info("NightLight: ARMED — stamping EffectsUpdated on placed run props " +
                             "at every day/night flip (+ settle x" +
                             $"{Tuning.SettlePasses}), anchored after EffectFlagSystem @ " +
                             $"{FlagInterval} frames; nothing else re-evaluates Night-gated " +
                             "lights on static props (light-refresh-mechanism.md)");
            }
            catch (Exception e)
            {
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiNightLightSystem)} EXCEPTION during load " +
                                  "setup — night-light refresh disabled until the next game " +
                                  $"load. Please report this stack:\n{e}");
                }
            }
        }

        protected override void OnUpdate()
        {
            try
            {
                if (m_Disabled || !m_Armed)
                    return;
                // No sign, run or lift of ours (ResortPresence) or no placed prop at all: nothing to stamp. m_HaveFlag stays false, so the first pass
                // after the first prop appears is the hygiene pass.
                if (!ResortPresence.Present || m_MemberQuery.IsEmptyIgnoreFilter)
                    return;

                bool night = m_EffectFlagSystem.GetData().m_IsNightTime;

                bool flip = m_HaveFlag && night != m_LastNight;
                bool firstPass = !m_HaveFlag;
                m_HaveFlag = true;
                m_LastNight = night;

                if (flip)
                    m_SettleRemaining = Tuning.SettlePasses;

                // First pass after load stamps once as hygiene: the fly-by hazard means a save can carry a wrongly-enabled light.
                if (!flip && !firstPass && m_SettleRemaining <= 0)
                    return;
                if (m_SettleRemaining > 0)
                    m_SettleRemaining--;

                int stamped = StampRunProps();
                if (flip || firstPass)
                {
                    Mod.Log.Info($"NightLight: {(night ? "-> NIGHT" : "-> DAY")}" +
                                 (firstPass ? " (initial hygiene pass)" : "") +
                                 $" — EffectsUpdated stamped on {stamped} run prop(s)" +
                                 (stamped == 0 ? " (none present — nothing to refresh)" : ""));
                }
            }
            catch (Exception e)
            {
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiNightLightSystem)} EXCEPTION — night-light " +
                                  "refresh disabled until the next game load. Please report " +
                                  $"this stack:\n{e}");
                }
            }
        }

        private int StampRunProps()
        {
            using (var toStamp = m_MemberQuery.ToEntityArray(Allocator.Temp))
            {
                if (toStamp.Length > 0)
                    EntityManager.AddComponent<EffectsUpdated>(toStamp);   // one structural change, not one per prop
                return toStamp.Length;
            }
        }

        private static string HexOf(float4 c)
        {
            return "#" + ((int)(c.x * 255f + 0.5f)).ToString("X2") + ((int)(c.y * 255f + 0.5f)).ToString("X2") +
                   ((int)(c.z * 255f + 0.5f)).ToString("X2") + " a" + c.w.ToString("0.##");
        }
    }
}
