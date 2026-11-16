// T222: the one snow pack step for a run texel and a cat track cell (RunSnowRules.PackStep, wired in round 2). The hand values use the
// pre-probe melt estimate (0.1092 m/h/C) as a fixed test parameter, so they do not move when MeltPerHourPerC is re-measured. Groups:
// - SP1: the melt and build rates (literal hand values from the injection's ramp and the fixed test melt).
// - SP2: the gun hysteresis on the pack's own stop depth, and the state after a load from the saved depth alone (T223).
// - SP3: one step: build to the stop depth, never lower deeper snow, melt then build in marginal air.
// - SP4: no hold: a warm spell melts made snow at the natural rate, a run texel is left to the game's melt.
// - SP5: one rule for both surfaces: a track cell (mod melt) and a texel (the same melt applied outside, as the game does) end equal.
// - SP6: T223: plumes and the bill follow the derived gun state from the first pass after a load.
// - SP7: the save clamp: a deeper base fits SkiTrackPack's i32 millimeters with no layout change.

using System;
using SkiResortMod.Systems;

namespace ResortBatch
{
    public static class SnowPackTests
    {
        private const string W = "SnowPack";

        private static bool Near(float a, float b, float tol = 1e-4f) => Math.Abs(a - b) <= tol;

        public static void Run(Func<bool, string, string, bool> check)
        {
            const float melt1 = 0.109227f;   // the pre-probe estimate: 0.00064 m per window per C x 10,922.67 frames per hour / 64 frames per window
            var today = RunSnowRules.PackParams.With(RunSnowRules.LegacyTrackStopDepthM, melt1);   // the 1.0.1 stop depth, for SP2c / SP3b / SP7
            var prop = RunSnowRules.PackParams.With(RunSnowRules.GunStopDepthM, melt1);
            check(RunSnowRules.PackParams.Default.StopDepthM == RunSnowRules.GunStopDepthM && RunSnowRules.PackParams.Default.MeltPerHourPerC == RunSnowRules.MeltPerHourPerC &&
                  prop.StopDepthM == 2f, W, "SP0 the live parameters: the 2.0 m base and the measured melt (" + RunSnowRules.MeltPerHourPerC + " m/h/C)");

            // SP1
            check(RunSnowRules.PackMeltPerHourM(today, 0f) == 0f && RunSnowRules.PackMeltPerHourM(today, -5f) == 0f
                  && RunSnowRules.PackMeltPerHourM(today, float.NaN) == 0f, W, "SP1b nothing melts at or below freezing, or on NaN air");
            check(Near(RunSnowRules.PackMeltPerHourM(today, 3f), 3f * melt1, 1e-5f), W, "SP1c linear in the degrees above freezing");
            // 0.004 x 1.125 x 0.35 x 170.667 = 0.2688 ; 0.004 x 1.125 x 2.0 x 170.667 = 1.536
            check(Near(RunSnowRules.PackBuildPerHourM(3.3f, 3.3f, RunSnowRules.MeanRampRate), 0.2688f, 1e-3f), W,
                  "SP1d the mean texel's guns add 0.27 m per hour at the threshold");
            check(Near(RunSnowRules.PackBuildPerHourM(-5f, 3.3f, RunSnowRules.MeanRampRate), 1.536f, 1e-3f), W,
                  "SP1e and 1.54 m per hour at full chill (4 C under the threshold or colder)");
            check(RunSnowRules.PackBuildPerHourM(3.4f, 3.3f, 1f) == 0f && RunSnowRules.PackBuildPerHourM(float.NaN, 3.3f, 1f) == 0f, W,
                  "SP1f no build above the threshold or on NaN air");

            // SP2
            var R = RunSnowRules.PackGun.Running;
            var S = RunSnowRules.PackGun.Stopped;
            var U = RunSnowRules.PackGun.Unknown;
            check(RunSnowRules.NextPackGun(prop, U, 1.97f) == S && RunSnowRules.NextPackGun(prop, R, 1.97f) == R
                  && RunSnowRules.NextPackGun(prop, S, 1.97f) == S, W, "SP2 inside the band (1.95..2.00 m) the state is kept, Unknown reads Stopped");
            check(RunSnowRules.NextPackGun(prop, S, 1.949f) == R && RunSnowRules.NextPackGun(prop, R, 2.00f) == S, W,
                  "SP2b under 1.95 m the guns restart, at 2.00 m they stop");
            check(RunSnowRules.NextPackGun(today, U, 0.93f) == S && RunSnowRules.NextPackGun(today, U, 0.90f) == R, W,
                  "SP2c today's stop depth: the band is 0.91..0.96 m");
            check(RunSnowRules.NextPackGun(prop, R, float.NaN) == R, W, "SP2d NaN keeps the state");
            check(RunSnowRules.PackGunAfterLoad(prop, 0.40f) == R && RunSnowRules.PackGunAfterLoad(prop, 1.98f) == S
                  && RunSnowRules.PackGunAfterLoad(prop, 2.5f) == S, W,
                  "SP2e after a load the saved depth alone decides: 0.40 m resumes, 1.98 m waits, natural 2.5 m stands");

            // SP3
            var r = RunSnowRules.PackStep(prop, 0f, -6f, true, R, 1f, RunSnowRules.MeanRampRate, true);
            check(Near(r.DepthM, 1.536f, 1e-3f) && r.GunsMaking, W, "SP3 one cold hour builds 1.54 m on bare ground (" + r.DepthM + ")");
            r = RunSnowRules.PackStep(prop, r.DepthM, -6f, true, R, 1f, RunSnowRules.MeanRampRate, true);
            check(Near(r.DepthM, 2.00f) && Near(r.BuiltM, 0.464f, 1e-3f), W, "SP3a and the second stops at the 2.00 m base (" + r.DepthM + ")");
            r = RunSnowRules.PackStep(today, 0f, -6f, true, R, 1f, RunSnowRules.MeanRampRate, true);
            check(Near(r.DepthM, 0.96f), W, "SP3b today's params stop at 0.96 m");
            r = RunSnowRules.PackStep(prop, 2.6f, -6f, true, R, 1f, RunSnowRules.MeanRampRate, false);
            check(r.DepthM == 2.6f && r.BuiltM == 0f && !r.GunsMaking, W, "SP3c deeper natural snow is never lowered and gets no made snow");
            // +2 C, margin 3.3: melt 2 x 0.10923 = 0.21845; chill (3.3 - 2) / 4 = 0.325, build 0.004 x 1.125 x (0.35 + 1.65 x 0.325) x 170.667
            // = 0.68065; from 0.5 m: 0.5 - 0.21845 + 0.68065 = 0.9622
            r = RunSnowRules.PackStep(prop, 0.5f, 2f, true, R, 1f, RunSnowRules.MeanRampRate, true);
            check(Near(r.DepthM, 0.9622f, 2e-3f) && Near(r.MeltedM, 0.21845f, 1e-3f), W,
                  "SP3d marginal air (+2 C): the melt comes off, the guns still gain (" + r.DepthM + ")");
            r = RunSnowRules.PackStep(prop, 0.5f, -6f, false, R, 1f, RunSnowRules.MeanRampRate, true);
            check(r.DepthM == 0.5f && !r.GunsMaking, W, "SP3e guns not available (off season, switch Off, no snowmaking): no build");
            r = RunSnowRules.PackStep(prop, 0.5f, -6f, true, R, 100f, RunSnowRules.MeanRampRate, true);
            check(Near(r.DepthM, 2.00f), W, "SP3f a jump in the frame counter is capped at 6 h and still stops at the base");

            // SP4: a 48 h spell at +4 C with the guns unable to run (above freezing + 3.3)
            float d = 2.00f;
            for (int h = 0; h < 48; h++)
                d = RunSnowRules.PackStep(prop, d, 4f, true, R, 1f, RunSnowRules.MeanRampRate, true).DepthM;
            check(d == 0f, W, "SP4 no hold: 2 m of made snow melts out within a 48 h spell at +4 C, as natural snow does");
            float hours = (2.00f - RunSnowRules.KeepDepthM) / (4f * melt1);
            check(Near(hours, 3.891f, 1e-2f), W, "SP4b and reaches the 0.30 m keep bar after 3.9 h at today's melt estimate (" + hours + " h)");
            r = RunSnowRules.PackStep(prop, 1.2f, 4f, true, R, 1f, RunSnowRules.MeanRampRate, false);
            check(r.DepthM == 1.2f && r.MeltedM == 0f, W, "SP4c a run texel: the step never melts, the game's shader does");

            // SP5: the same 30 h air series on a track cell and on a texel whose melt is applied outside at the same rate
            float[] air = { -8, -6, -4, -2, 0, 1, 2, 3, 4, 5, 6, 5, 3, 1, -1, -3, -5, -7, -2, 2, 4, 6, 8, 6, 3, 0, -2, -4, -6, -8 };
            float cell = 0f, tex = 0f;
            var gc = U;
            var gt = U;
            for (int i = 0; i < air.Length; i++)
            {
                gc = RunSnowRules.NextPackGun(prop, gc, cell);
                cell = RunSnowRules.PackStep(prop, cell, air[i], true, gc, 1f, RunSnowRules.MeanRampRate, true).DepthM;
                gt = RunSnowRules.NextPackGun(prop, gt, tex);
                float gameMelt = Math.Min(tex, RunSnowRules.PackMeltPerHourM(prop, air[i]));   // the shader, as calibrated
                tex = RunSnowRules.PackStep(prop, tex - gameMelt, air[i], true, gt, 1f, RunSnowRules.MeanRampRate, false).DepthM;
            }
            check(Near(cell, tex, 1e-4f), W, "SP5 one rule: the track cell and the texel end at the same depth (" + cell + " / " + tex + ")");

            // SP6: T223
            var g = RunSnowRules.PackGunAfterLoad(prop, 0.60f);
            check(RunSnowRules.PackGunsMaking(prop, g, -3f, true), W,
                  "SP6 a track reloaded under the stop depth in cold air bills snowmaking from the first pass (no runtime work counter)");
            g = RunSnowRules.PackGunAfterLoad(prop, 1.98f);
            check(!RunSnowRules.PackGunsMaking(prop, g, -3f, true), W, "SP6b one reloaded in the band shows no plume until it falls under 1.95 m");
            g = RunSnowRules.PackGunAfterLoad(prop, 0.60f);
            check(!RunSnowRules.PackGunsMaking(prop, g, 5f, true) && !RunSnowRules.PackGunsMaking(prop, g, -3f, false), W,
                  "SP6c warm air or no guns: no plume, no bill (there is no held state left to flash)");

            // SP7
            check(RunSnowRules.PackDepthToMm(2.0f) == 2000 && RunSnowRules.PackDepthFromMm(2000) == 2.0f, W,
                  "SP7 a 2 m pack round-trips SkiTrackPack's millimeters");
            // T223 edge 1's root: 0.91f + 0.05f is 0.96000004f, 960 mm reloads as 0.96f = 0.95999998f, so today's "depth < GunStopDepthM"
            // reads a full saved pack as still building. The pack step's stop test carries the merge's tolerance.
            float reloaded = 960 / 1000f;   // 1.0.1: TrackDepthFromMm(TrackDepthToMm(0.91f + 0.05f))
            check(reloaded < RunSnowRules.SkiableDepthM + RunSnowRules.GunRestartBandM, W, "SP7d (documents the 1.0.1 edge) a full 960 mm pack reloaded under its 0.96 m stop depth");
            check(RunSnowRules.PackGunAfterLoad(today, reloaded) == S, W, "SP7e the pack step reads the same reloaded pack as at the stop: no flash");
            check(RunSnowRules.PackGunAfterLoad(prop, RunSnowRules.PackDepthFromMm(RunSnowRules.PackDepthToMm(prop.StopDepthM))) == S, W,
                  "SP7f and a full 2 m pack round-trips to Stopped too");
            check(RunSnowRules.PackDepthToMm(float.NaN) == 0 && RunSnowRules.PackDepthToMm(20f) == 8000 && RunSnowRules.PackDepthFromMm(-5) == 0f, W,
                  "SP7c NaN saves 0, the clamp is the 8 m snow scale");
        }
    }
}
