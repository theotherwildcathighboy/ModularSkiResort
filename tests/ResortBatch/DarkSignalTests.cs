// Tests for the shared dark signal (DarkSignalRules.cs, T250).
//   DS-1 light hysteresis: dark below 0.20, light again from 0.30, no flicker between
//   DS-2 first look of a load, NaN
//   DS-3 clock fallback 17:00-07:00 and the source choice
//   DS-4 the light gate and the required flags it writes
// Oracles are literal hand values.

using System;
using SkiResortMod.Systems;
using R = SkiResortMod.Systems.DarkSignalRules;

namespace ResortBatch
{
    public static class DarkSignalTests
    {
        private static Func<bool, string, string, bool> s_Check;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            Hysteresis();
            FirstLook();
            Clock();
            Gate();
        }

        private static void C(bool ok, string where, string msg) => s_Check(ok, where, msg);

        private static void Hysteresis()
        {
            C(R.DarkBelow == 0.20f && R.LightFrom == 0.30f, "DS-1 the band", $"{R.DarkBelow} / {R.LightFrom}");
            // Dusk: the sun dims from day. Light until under 0.20
            bool dark = false;
            float[] dusk = { 0.9f, 0.5f, 0.31f, 0.25f, 0.2001f };
            foreach (float b in dusk)
            {
                dark = R.StepLight(dark, b);
                C(!dark, "DS-1 still light at dusk", $"{b}");
            }
            dark = R.StepLight(dark, 0.19f);
            C(dark, "DS-1 dark under 0.20", "0.19");
            // Hovering in the band does not flicker
            foreach (float b in new[] { 0.21f, 0.25f, 0.29f, 0.22f })
            {
                dark = R.StepLight(dark, b);
                C(dark, "DS-1 stays dark in the band", $"{b}");
            }
            dark = R.StepLight(dark, 0.30f);
            C(!dark, "DS-1 light again from 0.30", "0.30");
            foreach (float b in new[] { 0.29f, 0.21f })
            {
                dark = R.StepLight(dark, b);
                C(!dark, "DS-1 stays light in the band", $"{b}");
            }
            // Night: 0 is dark, NaN keeps the last value
            C(R.StepLight(false, 0f), "DS-1 night", "0");
            C(R.StepLight(true, float.NaN) && !R.StepLight(false, float.NaN), "DS-1 NaN keeps the value", "NaN");
        }

        private static void FirstLook()
        {
            C(R.FirstLight(0f) && R.FirstLight(0.24f), "DS-2 first look dark under the midpoint", "0 / 0.24");
            C(!R.FirstLight(0.25f) && !R.FirstLight(1f), "DS-2 first look light from the midpoint", "0.25 / 1");
            C(!R.FirstLight(float.NaN), "DS-2 NaN is light", "NaN");
        }

        private static void Clock()
        {
            // normalizedTime: hours / 24
            C(!R.ClockDark(16.99f / 24f), "DS-3 16:59 light", "");
            C(R.ClockDark(17f / 24f), "DS-3 17:00 dark", "");
            C(R.ClockDark(0f) && R.ClockDark(3f / 24f), "DS-3 midnight and 03:00 dark", "");
            C(R.ClockDark(6.99f / 24f), "DS-3 06:59 dark", "");
            C(!R.ClockDark(7f / 24f) && !R.ClockDark(12f / 24f), "DS-3 07:00 and noon light", "");
            C(R.ClockDark(1f + 18f / 24f), "DS-3 wraps above 1", "");
            C(!R.ClockDark(float.NaN), "DS-3 NaN light", "");
            C(R.SourceFor(true, true) == DarkSource.Light, "DS-3 the sun decides", "visual on");
            C(R.SourceFor(false, true) == DarkSource.Clock, "DS-3 visual cycle off: the clock", "the sun is pinned at 14:30");
            C(R.SourceFor(true, false) == DarkSource.Clock, "DS-3 no lighting: the clock", "");
        }

        private static void Gate()
        {
            C(R.GateFor(DarkSource.Light, true) == LightGate.On && R.GateFor(DarkSource.Light, false) == LightGate.Off,
                "DS-4 the sun moves the lights", "on / off");
            C(R.GateFor(DarkSource.Clock, true) == LightGate.Vanilla && R.GateFor(DarkSource.None, false) == LightGate.Vanilla,
                "DS-4 the clock leaves the game's Night flag", "vanilla");
            // Our effects require Night (8192) only
            C(R.RequiredFor(LightGate.Vanilla, 0x2000) == 0x2000, "DS-4 vanilla keeps Night", "");
            C(R.RequiredFor(LightGate.On, 0x2000) == 0, "DS-4 on: no condition", "");
            C(R.RequiredFor(LightGate.Off, 0x2000) == 0x1000, "DS-4 off: Occurring, never true on a prop", "");
            // Other bits stay; an effect that never required Night is never touched
            C(R.RequiredFor(LightGate.On, 0x2000 | 0x4) == 0x4 && R.RequiredFor(LightGate.Off, 0x2000 | 0x4) == (0x1000 | 0x4),
                "DS-4 other bits stay", "");
            C(R.RequiredFor(LightGate.Off, 0x4) == 0x4 && R.RequiredFor(LightGate.On, 0) == 0, "DS-4 not Night-gated: untouched", "");
            C(R.LightsOn(LightGate.Vanilla, true) && !R.LightsOn(LightGate.Vanilla, false), "DS-4 vanilla follows the flag", "");
            C(R.LightsOn(LightGate.On, false) && !R.LightsOn(LightGate.Off, true), "DS-4 the gate beats the flag", "");
        }
    }
}
