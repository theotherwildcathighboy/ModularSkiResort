// SkiResortHubUISystem.DrawRun.cs - the Run tile's option row (run types R3, DECISIONS 578, contract 33).
// One Run tile draws every piste. Groomed picks the prefab the area tool draws (SkiRunGroomedA or SkiRunNaturalA, the two looks of 577).
// Snow guns and Lights are the sliders a new groomed run starts with. SkiRunBindTriggerSystem seeds them onto the run the frame it is drawn
// (ModificationEnd, before its bind charge), so the fee includes their steps. The retired tiles (runNatural, runSnowmaking, runNight)
// stay in the catalog table (gate R16) but are not sent.
using System;
using Colossal.UI.Binding;
using Game.Prefabs;

namespace SkiResortMod.Systems
{
    public partial class SkiResortHubUISystem
    {
        /// <summary>`drawRun` (bindings.ts DrawRun): the option row's state. Active while the area tool draws one of our pistes.</summary>
        public struct HubDrawRun : IJsonWritable, IEquatable<HubDrawRun>
        {
            public bool Active, Groomed;
            public int GunSpacingM, LightSpacingM;

            public void Write(IJsonWriter writer)
            {
                writer.TypeBegin("SkiResortMod.DrawRun");
                writer.PropertyName("active");
                writer.Write(Active);
                writer.PropertyName("groomed");
                writer.Write(Groomed);
                writer.PropertyName("gunSpacingM");
                writer.Write(GunSpacingM);
                writer.PropertyName("lightSpacingM");
                writer.Write(LightSpacingM);
                writer.TypeEnd();
            }

            public bool Equals(HubDrawRun o) =>
                Active == o.Active && Groomed == o.Groomed && GunSpacingM == o.GunSpacingM && LightSpacingM == o.LightSpacingM;
            public override bool Equals(object obj) => obj is HubDrawRun o && Equals(o);
            public override int GetHashCode() => (GunSpacingM * 397) ^ LightSpacingM ^ (Groomed ? 1 : 0) ^ (Active ? 2 : 0);
        }

        private const string RunItemId = "runGroomed", NaturalItemId = "runNatural";

        private ValueBinding<HubDrawRun> m_DrawRunBinding;
        private bool m_DrawGroomed = true;
        private int m_DrawGunM = RunPropPlacerRules.SpacingOff, m_DrawLightM = RunPropPlacerRules.SpacingOff;

        /// <summary>A tile the strip no longer shows: the Run tile draws both looks, and snowmaking or lights are sliders.</summary>
        private static bool IsRetiredRunItem(string id) => id == NaturalItemId || id == "runSnowmaking" || id == "runNight";

        /// <summary>The Run tile draws the natural look while Groomed is off.</summary>
        private PrefabBase DrawPrefab(int item, PrefabBase prefab)
        {
            if (Items[item].Id != RunItemId || m_DrawGroomed)
                return prefab;
            int natural = IndexOfItem(NaturalItemId);
            return natural >= 0 && m_ItemPrefab[natural] != null ? m_ItemPrefab[natural] : prefab;
        }

        /// <summary>ActiveItemIndex for the strip: the natural look lights the Run tile.</summary>
        private static int StripItem(int item)
        {
            return item >= 0 && Items[item].Id == NaturalItemId ? IndexOfItem(RunItemId) : item;
        }

        private bool DrawRowActive => m_ActiveItem >= 0 && Items[m_ActiveItem].Id == RunItemId;

        private void PushDrawRun()
        {
            m_DrawRunBinding?.Update(new HubDrawRun
            {
                Active = DrawRowActive, Groomed = m_DrawGroomed, GunSpacingM = m_DrawGunM, LightSpacingM = m_DrawLightM,
            });
        }

        /// <summary>setDrawRun(key, value): "groomed" 0 / 1 (redraws with the other prefab while the tool is open). "guns" and "lights"
        /// are meters (RunPropPlacerRules.ClampSpacing, Off = 0).</summary>
        private void ApplyDrawRun(string key, int value, string source)
        {
            switch (key)
            {
                case "groomed":
                    m_DrawGroomed = value != 0;
                    if (DrawRowActive)
                    {
                        int item = IndexOfItem(RunItemId);
                        PrefabBase prefab = item >= 0 ? DrawPrefab(item, m_ItemPrefab[item]) : null;
                        if (prefab != null)
                            m_ToolSystem.ActivatePrefabTool(prefab);
                    }
                    break;
                case "guns":
                    m_DrawGunM = RunPropPlacerRules.ClampSpacing(value);
                    break;
                case "lights":
                    m_DrawLightM = RunPropPlacerRules.ClampSpacing(value);
                    break;
                default:
                    LastTriggerText = $"setDrawRun {key}: unknown key";
                    return;
            }
            Mod.Log.Info($"Hub [draw] {key} = {value}: groomed {m_DrawGroomed}, guns {m_DrawGunM} m, lights {m_DrawLightM} m ({source})");
            LastTriggerText = $"setDrawRun {key} {value}";
            PushDrawRun();
        }

        /// <summary>The sliders a run drawn this frame starts with (SkiRunBindTriggerSystem, ModificationEnd). True while the row is
        /// active, Groomed is on and a slider is above Off. A natural run carries none.</summary>
        internal bool TryGetDrawSeed(out int gunM, out int lightM)
        {
            gunM = m_DrawGunM;
            lightM = m_DrawLightM;
            return DrawRowActive && m_DrawGroomed &&
                   (gunM > RunPropPlacerRules.SpacingOff || lightM > RunPropPlacerRules.SpacingOff);
        }
    }
}
