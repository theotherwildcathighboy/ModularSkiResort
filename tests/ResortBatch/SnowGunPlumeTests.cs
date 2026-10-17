// SnowGunPlumeTests — T46 (2026-10-17; docs/research/snow-gun-plume-2026-10-17.md, build note
// docs/research/field/2026-10-15/build/T46-snow-gun-plume.md): the pure rules in SnowGunPlumeRules.cs, the ONE predicate the
// snow injection (raise / bill) and the plume system (vapour) share. `public static class SnowGunPlumeTests` with
// `public static void Run(Func<bool, string, string, bool> check)`, called from Program.Main like the other files.
//   GP-1  constants: stop 0.60 m above the 0.35 m guaranteed base and four open bars; band 0.05 m; restart above the
//         guaranteed base; the at-target tolerance is the merge's half quantum (0.06 mm); lease 1.5 game h; cap 256.
//   GP-2  the pack measure: ratio clamp, fringe at its own target, deep natural reads "at target", NaN when unmeasured.
//   GP-3  THE CASES Richard asked for: cold + shallow -> on; cold + at target -> off; cold + deep natural -> off; warm -> off;
//         protection-only pass -> off; hysteresis (no flicker across the stop depth).
//   GP-4  the plume of one gun: setting, run flag, own elevation (the thermal line crosses a run).
//   GP-5  the lease: write / flip decisions, saturation, the game's own frame < end test.
//   GP-6  the research numbers (§8): game hours from bare to the stop depth at -5 C and at the gate, mean and slowest texel.
// Oracles are literal hand values (the comments show the arithmetic).

using System;
using SkiResortMod.Systems;
using S = SkiResortMod.Systems.SnowGunPlumeRules;

namespace ResortBatch
{
    public static class SnowGunPlumeTests
    {
        private static Func<bool, string, string, bool> s_Check;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            Constants();
            Pack();
            Cases();
            Plume();
            Lease();
            Research();
        }

        private static bool Near(double a, double b, double tol) => Math.Abs(a - b) <= tol;

        private static void C(bool ok, string where, string msg) => s_Check(ok, where, msg);

        // GP-1 (9)
        private static void Constants()
        {
            const string W = "plume GP-1 constants";
            C(RunSnowRules.GunStopDepthM == 0.60f, W, "gun stop depth 0.60 m");
            C(RunSnowRules.GunRestartBandM == 0.05f, W, "restart band 0.05 m");
            C(RunSnowRules.GunStopDepthM > RunSnowRules.GunTargetDepthM, W, "the stop depth is above the 0.35 m base the guns guarantee");
            C(RunSnowRules.GunStopDepthM >= 4f * RunSnowRules.SkiableDepthM, W, "the stop depth is at least four open bars (0.60 = 4 x 0.15)");
            C(S.RestartBelowM > RunSnowRules.GunTargetDepthM, W, "guns restart (0.55 m) long before the pack falls to the guaranteed base");
            C(Near(S.AtTargetToleranceM, 0.5 / 65535.0 * 8.0, 1e-9), W, "at-target tolerance = the merge's half quantum x 8 m (0.061 mm)");
            C(Near(S.StopAtM, 0.60 - 0.5 / 65535.0 * 8.0, 1e-6) && Near(S.RestartBelowM, 0.55, 1e-6), W, "stop at 0.59994 m, restart below 0.55 m");
            C(Near(S.PlumeLeaseFrames / RunSnowRules.FramesPerGameHour, 1.5, 1e-3) && S.PlumeRenewFrames * 2 == S.PlumeLeaseFrames,
              W, "lease 16384 frames = 1.5 game h, renewed at half");
            C(S.PlumeMaxCount == 256 && S.PlumeIntervalFrames == 64, W, "plume cap 256 (the EffectPrefab's m_MaxCount), pass every 64 frames");
        }

        // GP-2 (8). Normalized units: 0.60 m = 0.075, 0.30 m = 0.0375, a fringe target 0.02 m = 0.0025.
        private static void Pack()
        {
            const string W = "plume GP-2 pack";
            float full = 0.60f / 8f, half = 0.30f / 8f, fringe = 0.02f / 8f;
            C(S.PackTerm(half, full) == half, W, "a shallow texel counts its depth");
            C(S.PackTerm(1.9f / 8f, full) == full, W, "a 1.9 m natural texel counts at its target (deep natural = at target)");
            C(S.PackTerm(0.01f / 8f, fringe) == 0.01f / 8f && S.PackTerm(0.05f / 8f, fringe) == fringe, W, "a fringe texel counts against its own 0.02 m target");
            // Two texels, one at 0.30 m and one deep natural: (0.0375 + 0.075) / (0.075 + 0.075) = 0.75 -> 0.45 m.
            C(Near(S.PackDepthM(0.15, 0.1125), 0.45, 1e-6), W, "half-built + deep natural reads 0.45 m (ratio 0.75)");
            C(Near(S.PackDepthM(0.075, 0.075), 0.60, 1e-6), W, "fully built reads the stop depth exactly");
            C(S.PackDepthM(0.075, 0.2) == 0.60f, W, "the ratio clamps at 1");
            C(float.IsNaN(S.PackDepthM(0.0, 0.0)) && float.IsNaN(S.PackDepthM(-1.0, 0.0)), W, "nothing measured -> NaN");
            C(float.IsNaN(S.PackDepthM(0.1, double.NaN)), W, "a NaN sum -> NaN");
        }

        // GP-3 (17): one run, pass by pass. running = GunsRunning(state the fold used, cold); the fold uses the state stepped
        // after the previous pass.
        private static void Cases()
        {
            const string W = "plume GP-3 cases";
            var U = S.GunState.Unknown;
            var R = S.GunState.Running;
            var P = S.GunState.Stopped;

            // cold + shallow -> on (bare run, first measurement of the load).
            var st = S.NextGunState(U, 0.0f);
            C(st == R, W, "bare + measured -> Running");
            C(S.GunsRunning(st, 120) && S.RaisesThisPass(st), W, "cold + shallow -> guns run (raise, bill, plume)");
            // cold + at target -> off.
            st = S.NextGunState(R, 0.60f);
            C(st == P, W, "pack at 0.60 m -> Stopped");
            C(!S.GunsRunning(st, 120) && !S.RaisesThisPass(st), W, "cold + at target -> guns off (no raise, no bill, no plume)");
            C(S.NextGunState(R, 0.60f - 0.00005f) == P, W, "within the half-quantum tolerance counts as at target");
            // cold + deep natural -> off (a February pack: every texel above its target reads at target).
            st = S.NextGunState(U, S.PackDepthM(0.075 * 1000, 0.075 * 1000));
            C(st == P && !S.GunsRunning(st, 5000), W, "cold + deep natural (first measurement) -> off");
            C(S.NextGunState(R, S.PackDepthM(0.075, 0.075)) == P, W, "running guns stop on a natural dump that reaches the stop depth");
            // warm -> off (no cold texel; the state may be Running from the night before).
            C(!S.GunsRunning(R, 0), W, "warm (no cold texel) -> off even while Running");
            C(!S.GunsRunning(S.NextGunState(R, float.NaN), 0), W, "warm pass, nothing measured -> state kept, still off");
            // protection-only pass: warm texels held by protection are never cold -> cold 0 -> off.
            C(!S.GunsRunning(R, 0) && !S.GunsRunning(P, 0), W, "a protection-only pass (cold 0) -> off in any state");
            // Unknown never raises or plumes.
            C(!S.RaisesThisPass(U) && !S.GunsRunning(U, 999), W, "Unknown (no measurement yet) -> no raise, no plume");
            C(S.NextGunState(U, float.NaN) == U, W, "Unknown stays Unknown until a measurement");
            // Hysteresis: build up, stop, melt slowly, restart only below 0.55, stop again at 0.60.
            float[] packs = { 0.10f, 0.40f, 0.58f, 0.59f, 0.60f, 0.59f, 0.57f, 0.556f, 0.55f, 0.549f, 0.56f, 0.59f, 0.60f };
            var expect = new[] { R, R, R, R, P, P, P, P, P, R, R, R, P };
            st = U;
            bool seqOk = true;
            int flipsSeen = 0;
            for (int i = 0; i < packs.Length; i++)
            {
                var next = S.NextGunState(st, packs[i]);
                if (next != st)
                    flipsSeen++;
                st = next;
                if (st != expect[i])
                    seqOk = false;
            }
            C(seqOk, W, "build 0.10 -> 0.60 runs, stops at 0.60, holds Stopped from 0.59 down to 0.55, restarts at 0.549, stops at 0.60");
            C(flipsSeen == 4, W, "exactly 4 flips over the sequence (Unknown->Running, stop, restart, stop): no flicker across the stop depth");
            C(S.NextGunState(U, 0.57f) == P, W, "first measurement inside the band (0.57 m) -> Stopped (waits for < 0.55)");
            C(S.NextGunState(R, 0.57f) == R && S.NextGunState(P, 0.57f) == P, W, "inside the band the state is kept");
        }

        // GP-4 (7)
        private static void Plume()
        {
            const string W = "plume GP-4 plume";
            float thr = 0f + RunSnowRules.DefaultSnowmakingMarginC;   // freezing 0 + 3.3
            C(S.PlumeOn(true, true, -5f, thr), W, "setting on, guns running, gun at -5 C -> plume");
            C(S.PlumeOn(true, true, 3.3f, thr), W, "gun exactly at the gate (3.3 C) -> plume (<=, the fold's test)");
            C(!S.PlumeOn(true, true, 3.4f, thr), W, "gun below the snow line (3.4 C) on a running run -> no plume");
            C(!S.PlumeOn(true, false, -10f, thr), W, "guns not running -> no plume, however cold");
            C(!S.PlumeOn(false, true, -10f, thr), W, "Options off -> no plume");
            C(!S.PlumeOn(true, true, float.NaN, thr), W, "NaN temperature reads warm");
            // The thermal line crosses a run: lapse 0.006 C/m (illustrative), ambient 5 C at 0 m -> the gate (3.3 C) sits
            // at 283 m: a top gun at 400 m (2.6 C) plumes, a bottom gun at 200 m (3.8 C) does not.
            float top = 5f - 400f * 0.006f, bottom = 5f - 200f * 0.006f;
            C(S.PlumeOn(true, true, top, thr) && !S.PlumeOn(true, true, bottom, thr), W, "top gun plumes, bottom gun does not");
        }

        // GP-5 (10)
        private static void Lease()
        {
            const string W = "plume GP-5 lease";
            uint f = 100000;
            C(S.LeaseEnd(f) == f + 16384, W, "lease ends 16384 frames ahead");
            C(S.LeaseEnd(uint.MaxValue - 10) == uint.MaxValue, W, "the lease saturates at uint.MaxValue");
            C(!S.LeaseLive(false, 999999, f) && !S.LeaseLive(true, f, f) && S.LeaseLive(true, f + 1, f), W,
              "live only with the component and frame < end (EffectControlData.cs:422)");
            C(S.NeedsWrite(true, false, 0, f) && S.Flips(true, false, 0, f), W, "on, no component -> write and flip");
            C(!S.NeedsWrite(true, true, f + 9000, f) && !S.Flips(true, true, f + 9000, f), W, "on, 9000 frames left -> nothing to do");
            C(S.NeedsWrite(true, true, f + 8000, f) && !S.Flips(true, true, f + 8000, f), W, "on, 8000 left (< 8192) -> renew without a flip");
            C(S.NeedsWrite(true, true, f - 1, f) && S.Flips(true, true, f - 1, f), W, "on, lease expired -> write and flip");
            C(S.NeedsWrite(false, true, f + 5, f) && S.Flips(false, true, f + 5, f), W, "off with a live lease -> cut and flip");
            C(!S.NeedsWrite(false, true, 0, f) && !S.NeedsWrite(false, false, 0, f), W, "off and already off -> nothing to do");
            C(!S.Flips(false, true, f, f), W, "a lease ending exactly now is already off (no flip)");
        }

        // GP-6 (6): GunWindowsToReach (64-frame windows), 4096 windows = 24 game h. Step = 0.004 x rate x (0.35 + 1.65 x chill).
        private static void Research()
        {
            const string W = "plume GP-6 research";
            float thr = RunSnowRules.DefaultSnowmakingMarginC;
            float slow = RunSnowRules.RampRateMin;                               // 0.25
            float mean = RunSnowRules.RampRateMin + RunSnowRules.RampRateSpan * 0.5f;   // 1.125
            // -5 C: chill = (3.3 + 5) / 4 > 1 -> 1, output 2.0. Slowest 0.60 / (0.004 x 0.25 x 2) = 300 windows
            // = 300 x 64 / 10922.67 = 1.76 game h. Mean 0.60 / 0.009 = 66.7 windows = 0.39 h.
            double hSlow = RunSnowRules.WindowsToGameHours(RunSnowRules.GunWindowsToReach(RunSnowRules.GunStopDepthM, -5f, thr, slow));
            double hMean = RunSnowRules.WindowsToGameHours(RunSnowRules.GunWindowsToReach(RunSnowRules.GunStopDepthM, -5f, thr, mean));
            C(Near(hSlow, 300 * 64 / 10922.6667, 0.01), W, "-5 C, slowest texel to 0.60 m: 1.76 game h (" + hSlow + ")");
            C(Near(hMean, 66.6667 * 64 / 10922.6667, 0.01), W, "-5 C, mean texel to 0.60 m: 0.39 game h (" + hMean + ")");
            // At the gate (3.3 C): output 0.35. Slowest 0.60 / 0.00035 = 1714 windows = 10.0 h; mean 0.60 / 0.001575 = 381 = 2.23 h.
            double gSlow = RunSnowRules.WindowsToGameHours(RunSnowRules.GunWindowsToReach(RunSnowRules.GunStopDepthM, thr, thr, slow));
            double gMean = RunSnowRules.WindowsToGameHours(RunSnowRules.GunWindowsToReach(RunSnowRules.GunStopDepthM, thr, thr, mean));
            C(Near(gSlow, 1714.29 * 64 / 10922.6667, 0.02), W, "at the gate, slowest texel to 0.60 m: 10.0 game h (" + gSlow + ")");
            C(Near(gMean, 380.95 * 64 / 10922.6667, 0.02), W, "at the gate, mean texel to 0.60 m: 2.23 game h (" + gMean + ")");
            // A restart from 0.55: the band (0.05 m) at -5 C, slowest: 25 windows = 0.15 h; at the gate 143 windows = 0.84 h.
            double rSlow = RunSnowRules.WindowsToGameHours(RunSnowRules.GunWindowsToReach(RunSnowRules.GunRestartBandM, -5f, thr, slow));
            double rGate = RunSnowRules.WindowsToGameHours(RunSnowRules.GunWindowsToReach(RunSnowRules.GunRestartBandM, thr, thr, slow));
            C(Near(rSlow, 25 * 64 / 10922.6667, 0.01) && Near(rGate, 142.86 * 64 / 10922.6667, 0.01), W,
              "a restart refills the 0.05 m band in 0.15 game h at -5 C and 0.84 h at the gate (slowest texel)");
            // The 0.35 m base keeps its S26 promise: unchanged by T46.
            C(RunSnowRules.WindowsToGameHours(RunSnowRules.GunWindowsToReach(RunSnowRules.GunTargetDepthM, thr, thr, slow)) <= 8f, W,
              "the 0.35 m base is still reached within one night at the gate (S26 unchanged)");
        }
    }
}
