// Tests for the pure rules in SnowGunPlumeRules.cs. Snow injection (raise / bill) and the plume system (vapor) share the one predicate
// there. Program.Main calls Run(check) like the other test files.
//   GP-1 constants   GP-2 pack measure   GP-3 gun state cases and hysteresis   GP-4 plume of one gun
//   GP-5 lease   GP-6 hours from bare to the stop depth, and the gun season
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

        // GP-1
        private static void Constants()
        {
            const string W = "plume GP-1 constants";
            // Guns run while the run is under 3 ft (0.91 m) and top it up to 3 ft + the band.
            C(Near(RunSnowRules.GunStopDepthM, 0.96, 1e-6), W, "T133 gun stop depth 0.96 m (3 ft + the band)");
            C(RunSnowRules.GunRestartBandM == 0.05f, W, "restart band 0.05 m");
            C(RunSnowRules.GunStopDepthM > RunSnowRules.GunTargetDepthM, W, "the stop depth is above the 3 ft base the guns guarantee");
            C(Near(S.RestartBelowM, RunSnowRules.SkiableDepthM, 1e-6), W, "T133 the guns restart exactly below the open bar (3 ft): until the run reaches 3 ft");
            C(S.StopAtM > RunSnowRules.SkiableDepthM, W, "T133 a pack the guns stopped on is open (stop above the bar)");
            C(Near(S.AtTargetToleranceM, 0.5 / 65535.0 * 8.0, 1e-9), W, "at-target tolerance = the merge's half quantum x 8 m (0.061 mm)");
            C(Near(S.StopAtM, 0.96 - 0.5 / 65535.0 * 8.0, 1e-6) && Near(S.RestartBelowM, 0.91, 1e-6), W, "stop at 0.95994 m, restart below 0.91 m");
            C(Near(S.PlumeLeaseFrames / RunSnowRules.FramesPerGameHour, 1.5, 1e-3) && S.PlumeRenewFrames * 2 == S.PlumeLeaseFrames,
              W, "lease 16384 frames = 1.5 game h, renewed at half");
            C(S.PlumeMaxCount == 256 && S.PlumeIntervalFrames == 64, W, "plume cap 256 (the EffectPrefab's m_MaxCount), pass every 64 frames");
        }

        // GP-2. Normalized units: 0.60 m = 0.075, 0.30 m = 0.0375, a fringe target 0.02 m = 0.0025.
        private static void Pack()
        {
            const string W = "plume GP-2 pack";
            float full = 0.60f / 8f, half = 0.30f / 8f, fringe = 0.02f / 8f;
            C(S.PackTerm(half, full) == half, W, "a shallow texel counts its depth");
            C(S.PackTerm(1.9f / 8f, full) == full, W, "a 1.9 m natural texel counts at its target (deep natural = at target)");
            C(S.PackTerm(0.01f / 8f, fringe) == 0.01f / 8f && S.PackTerm(0.05f / 8f, fringe) == fringe, W, "a fringe texel counts against its own 0.02 m target");
            // Two texels, one at 0.30 m and one deep natural (sums in units of 0.60 / 8): ratio 0.75 -> 0.75 x the stop depth.
            C(Near(S.PackDepthM(0.15, 0.1125), 0.75 * RunSnowRules.GunStopDepthM, 1e-6), W, "half-built + deep natural reads 0.75 x the stop depth (ratio 0.75)");
            C(Near(S.PackDepthM(0.075, 0.075), RunSnowRules.GunStopDepthM, 1e-6), W, "fully built reads the stop depth exactly");
            C(S.PackDepthM(0.075, 0.2) == RunSnowRules.GunStopDepthM, W, "the ratio clamps at 1");
            C(float.IsNaN(S.PackDepthM(0.0, 0.0)) && float.IsNaN(S.PackDepthM(-1.0, 0.0)), W, "nothing measured -> NaN");
            C(float.IsNaN(S.PackDepthM(0.1, double.NaN)), W, "a NaN sum -> NaN");
        }

        // GP-3: one run, pass by pass. running = GunsRunning(state the fold used, cold). The fold uses the state stepped
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
            float stop = RunSnowRules.GunStopDepthM;   // restart below 0.91 m
            st = S.NextGunState(R, stop);
            C(st == P, W, "pack at the stop depth -> Stopped");
            C(!S.GunsRunning(st, 120) && !S.RaisesThisPass(st), W, "cold + at target -> guns off (no raise, no bill, no plume)");
            C(S.NextGunState(R, stop - 0.00005f) == P, W, "within the half-quantum tolerance counts as at target");
            // cold + deep natural -> off (a February pack: every texel above its target reads at target).
            st = S.NextGunState(U, S.PackDepthM(0.075 * 1000, 0.075 * 1000));
            C(st == P && !S.GunsRunning(st, 5000), W, "cold + deep natural (first measurement) -> off");
            C(S.NextGunState(R, S.PackDepthM(0.075, 0.075)) == P, W, "running guns stop on a natural dump that reaches the stop depth");
            // warm -> off (no cold texel). The state may be Running from the night before.
            C(!S.GunsRunning(R, 0), W, "warm (no cold texel) -> off even while Running");
            C(!S.GunsRunning(S.NextGunState(R, float.NaN), 0), W, "warm pass, nothing measured -> state kept, still off");
            // protection-only pass: warm texels held by protection are never cold -> cold 0 -> off.
            C(!S.GunsRunning(R, 0) && !S.GunsRunning(P, 0), W, "a protection-only pass (cold 0) -> off in any state");
            // Unknown never raises or plumes.
            C(!S.RaisesThisPass(U) && !S.GunsRunning(U, 999), W, "Unknown (no measurement yet) -> no raise, no plume");
            C(S.NextGunState(U, float.NaN) == U, W, "Unknown stays Unknown until a measurement");
            // Hysteresis: build up, stop at 0.96, melt slowly, restart only below 0.91 (3 ft), stop again at 0.96.
            float[] packs = { 0.10f, 0.40f, 0.94f, 0.95f, stop, 0.95f, 0.93f, 0.916f, 0.911f, 0.905f, 0.92f, 0.95f, stop };
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
            C(seqOk, W, "build 0.10 -> 0.96 runs, stops at 0.96, holds Stopped from 0.95 down to 0.911, restarts at 0.905, stops at 0.96");
            C(flipsSeen == 4, W, "exactly 4 flips over the sequence (Unknown->Running, stop, restart, stop): no flicker across the stop depth");
            C(S.NextGunState(U, 0.93f) == P, W, "first measurement inside the band (0.93 m) -> Stopped (waits for < 0.91)");
            C(S.NextGunState(R, 0.93f) == R && S.NextGunState(P, 0.93f) == P, W, "inside the band the state is kept");
        }

        // GP-4
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
            // The thermal line crosses a run. Lapse 0.006 C/m (illustrative), ambient 5 C at 0 m -> the gate (3.3 C) sits
            // at 283 m. A top gun at 400 m (2.6 C) plumes. A bottom gun at 200 m (3.8 C) does not.
            float top = 5f - 400f * 0.006f, bottom = 5f - 200f * 0.006f;
            C(S.PlumeOn(true, true, top, thr) && !S.PlumeOn(true, true, bottom, thr), W, "top gun plumes, bottom gun does not");
        }

        // GP-5
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

        // GP-6: GunWindowsToReach (64-frame windows), 4096 windows = 24 game h. Step = 0.004 x rate x (0.35 + 1.65 x chill).
        private static void Research()
        {
            const string W = "plume GP-6 research";
            float thr = RunSnowRules.DefaultSnowmakingMarginC;
            float slow = RunSnowRules.RampRateMin;                               // 0.25
            float mean = RunSnowRules.RampRateMin + RunSnowRules.RampRateSpan * 0.5f;   // 1.125
            // Stop depth 0.96 m. -5 C: chill = (3.3 + 5) / 4 > 1 -> 1, output 2.0. Slowest 0.96 / (0.004 x 0.25 x 2) = 480
            // windows = 480 x 64 / 10922.67 = 2.81 game h. Mean 0.96 / 0.009 = 106.7 windows = 0.63 h.
            double hSlow = RunSnowRules.WindowsToGameHours(RunSnowRules.GunWindowsToReach(RunSnowRules.GunStopDepthM, -5f, thr, slow));
            double hMean = RunSnowRules.WindowsToGameHours(RunSnowRules.GunWindowsToReach(RunSnowRules.GunStopDepthM, -5f, thr, mean));
            C(Near(hSlow, 480 * 64 / 10922.6667, 0.01), W, "-5 C, slowest texel to 0.96 m: 2.81 game h (" + hSlow + ")");
            C(Near(hMean, 106.6667 * 64 / 10922.6667, 0.01), W, "-5 C, mean texel to 0.96 m: 0.63 game h (" + hMean + ")");
            // At the gate (3.3 C): output 0.35. Slowest 0.96 / 0.00035 = 2743 windows = 16.1 h. Mean 0.96 / 0.001575 = 610 = 3.57 h.
            double gSlow = RunSnowRules.WindowsToGameHours(RunSnowRules.GunWindowsToReach(RunSnowRules.GunStopDepthM, thr, thr, slow));
            double gMean = RunSnowRules.WindowsToGameHours(RunSnowRules.GunWindowsToReach(RunSnowRules.GunStopDepthM, thr, thr, mean));
            C(Near(gSlow, 2742.86 * 64 / 10922.6667, 0.02), W, "at the gate, slowest texel to 0.96 m: 16.1 game h (" + gSlow + ")");
            C(Near(gMean, 609.52 * 64 / 10922.6667, 0.02), W, "at the gate, mean texel to 0.96 m: 3.57 game h (" + gMean + ")");
            // A restart from 0.91: the band (0.05 m) at -5 C, slowest: 25 windows = 0.15 h. At the gate 143 windows = 0.84 h.
            double rSlow = RunSnowRules.WindowsToGameHours(RunSnowRules.GunWindowsToReach(RunSnowRules.GunRestartBandM, -5f, thr, slow));
            double rGate = RunSnowRules.WindowsToGameHours(RunSnowRules.GunWindowsToReach(RunSnowRules.GunRestartBandM, thr, thr, slow));
            C(Near(rSlow, 25 * 64 / 10922.6667, 0.01) && Near(rGate, 142.86 * 64 / 10922.6667, 0.01), W,
              "a restart refills the 0.05 m band in 0.15 game h at -5 C and 0.84 h at the gate (slowest texel)");
            // The 3 ft base: the mean texel reaches it within one night (8 game h) even at the gate. The slowest texel reaches it within a game day.
            C(RunSnowRules.WindowsToGameHours(RunSnowRules.GunWindowsToReach(RunSnowRules.GunTargetDepthM, thr, thr, mean)) <= 8f, W,
              "T133 the 3 ft base: mean texel within one night at the gate");
            C(RunSnowRules.WindowsToGameHours(RunSnowRules.GunWindowsToReach(RunSnowRules.GunTargetDepthM, thr, thr, slow)) <= 24f, W,
              "T133 the 3 ft base: slowest texel within one game day at the gate");
            // The gun season (October to March around a December winter). A vanilla winter / summer name decides first.
            C(S.InGunSeason("SeasonWinter", 6.5f / 12f), W, "T135 a season named winter is in, whatever the calendar (southern map)");
            C(!S.InGunSeason("SeasonSummer", 0.5f / 12f), W, "T135 a season named summer is out");
            C(!S.InGunSeason("SeasonSpring", 4.8f / 12f), W, "T135 late May (spring) is out: the 2026-10-20 guns in May");
            C(!S.InGunSeason("SeasonSpring", 3.5f / 12f), W, "T135 mid April is out");
            C(S.InGunSeason("SeasonSpring", 2.5f / 12f), W, "T135 mid March is in");
            C(!S.InGunSeason("SeasonAutumn", 8.5f / 12f), W, "T135 September is out");
            C(S.InGunSeason("SeasonAutumn", 9.0f / 12f) && S.InGunSeason("SeasonAutumn", 10.5f / 12f), W, "T135 October and November are in");
            C(S.InGunSeason("", 0.5f / 12f) && S.InGunSeason(null, 11.5f / 12f) && !S.InGunSeason(null, 6f / 12f), W,
              "T135 no season name: the calendar (January in, December in, July out)");
            // A map whose winter is June to August: the window moves with it (field test 2026-10-31).
            const float WS = 5f / 12f, WE = 8f / 12f;
            C(S.InGunSeason("SeasonAutumn", 3.0f / 12f, WS, WE) && S.InGunSeason("SeasonAutumn", 4.5f / 12f, WS, WE), W, "southern winter: April and May are in");
            C(!S.InGunSeason("SeasonAutumn", 2.5f / 12f, WS, WE), W, "southern winter: warm March is out");
            C(S.InGunSeason("SeasonSpring", 8.5f / 12f, WS, WE) && !S.InGunSeason("SeasonSpring", 9.5f / 12f, WS, WE), W, "southern winter: September in, October out");
            C(S.InGunSeason("SeasonSpring", 2.5f / 12f, 11f / 12f, 14f / 12f) && !S.InGunSeason("SeasonSpring", 3.5f / 12f, 11f / 12f, 14f / 12f), W, "December winter: the window wraps the year");
            C(!S.RaisesThisPass(S.GunState.Running, false) && S.RaisesThisPass(S.GunState.Running, true), W, "T135 out of season the guns raise nothing");
            C(!S.GunsRunning(S.GunState.Running, 100, false) && S.GunsRunning(S.GunState.Running, 100, true), W, "T135 out of season no gun runs (no plume)");
        }
    }
}
