// Round 2 (snow REPORT section 6): a run and a cat track over the same vertical open together at any sim speed. The run raise
// (RunSnowRules.PackRaiseTexelM, one call per evaluated pass with StepScale of the frames since the last pass) against the track model
// (StepTrackPack, dt from the same frames). The pass cadence is the injection's: a pass at the first sim frame with >= 64 frames since the
// last pass and >= MinPassIntervalS of wall clock. At 60 sim frames per wall second per speed step, that is 64 frames a pass at speeds 1
// and 2 and 240 at speed 8. A heavy city stretches the wall interval of a pass (the check runs once per rendered frame, and the pass costs
// time): the round 2 soak saw 1.25-1.72 passes a second at speed 8, 280-384 frames a pass. Groups:
// - SS1: StepScale: one window at the speed-1 cadence and on the first pass of a load, sim time at speed 8, the 6 h cap on a jump.
// - SS2: the cause: with the pre-round-2 cap (4 windows) a run at speed 8 with a slow frame rate opens far behind the track.
// - SS3: the fix: at speeds 1, 2 and 8 (and 8 slow) the run opens within minutes of the track, at full chill (-5 C) and at -0.2 C
//        (chill 0.875). Above freezing the measured melt law (0.0425 x air / depth) melts thin snow faster than the guns build, so neither
//        surface builds from bare there; that law is fitted on 0.3-2.0 m patches, so it is not a check here.
// - SS4: no catch-up burst: a pause adds no frames, and the first pass after a load raises one window, however old the save.
// - SS5: a run's share leaves out the texels the edge noise dropped (never raised), with a fallback for a run with nothing else.
// - SS6: a run's mean ramp rate is the track's (RunRateScale), whatever coarse noise cells it sits on.

using System;
using System.Collections.Generic;
using SkiResortMod.Systems;

namespace ResortBatch
{
    public static class SnowSpeedTests
    {
        private const string W = "SnowSpeed";
        private const float Bar = RunSnowRules.SkiableDepthM;

        /// <summary>Sim frames per evaluated pass at <paramref name="speed"/> with <paramref name="intervalS"/> of wall clock between passes
        /// (at least MinPassIntervalS).</summary>
        private static uint FramesPerPass(int speed, float intervalS)
        {
            float framesPerWallS = 60f * speed;
            uint byWall = (uint)Math.Ceiling(Math.Max(RunSnowRules.MinPassIntervalS, intervalS) * framesPerWallS);
            return Math.Max((uint)RunSnowRules.TopupIntervalFrames, byWall);
        }

        /// <summary>The per-texel noise rates of a run: the merge's 0.65 x coarse + 0.35 x fine hash, here a 20 x 20 grid of both octaves.</summary>
        private static List<float> TexelRates()
        {
            var r = new List<float>();
            for (int i = 0; i < 20; i++)
                for (int j = 0; j < 20; j++)
                    r.Add(RunSnowRules.RampRateMin + RunSnowRules.RampRateSpan * (0.65f * (i + 0.5f) / 20f + 0.35f * (j + 0.5f) / 20f));
            return r;
        }

        /// <summary>Game hours until the run's open share (texels at the bar) reaches OpenAtShare. <paramref name="maxScale"/>: the step
        /// cap to apply (the live MaxStepScale, or the pre-round-2 4).</summary>
        private static float RunOpensAtH(uint framesPerPass, float airC, float maxScale, List<float> rates = null)
        {
            var p = RunSnowRules.PackParams.Default;
            float threshold = p.FreezingC + p.MarginC;
            float chill = Math.Max(0f, Math.Min(1f, (threshold - airC) / RunSnowRules.RampChillSpanC));
            rates = rates ?? TexelRates();
            var depth = new float[rates.Count];
            float dtH = framesPerPass / RunSnowRules.FramesPerGameHour;
            float scale = Math.Min(RunSnowRules.StepScale(framesPerPass), maxScale);
            for (int pass = 1; pass < 200000; pass++)
            {
                int open = 0;
                for (int i = 0; i < depth.Length; i++)
                {
                    // the game melts the texel between passes (the probe's rule), then the pass raises it
                    depth[i] -= RunSnowRules.PackMeltM(p, depth[i], airC, dtH);
                    depth[i] = RunSnowRules.PackRaiseTexelM(depth[i], RunSnowRules.GunStopDepthM, chill, rates[i], scale);
                    if (depth[i] >= Bar - 1e-4f)
                        open++;
                }
                if (open >= RunSnowRules.OpenAtShare * depth.Length)
                    return pass * dtH;
            }
            return float.PositiveInfinity;
        }

        /// <summary>Game hours until the track over the same (flat) vertical opens: its cells at the bar on OpenAtShare of the vertical.</summary>
        private static float TrackOpensAtH(uint framesPerPass, float airC)
        {
            var p = RunSnowRules.PackParams.Default;
            var t = default(RunSnowRules.TrackMadeSnow);
            t.Gun = RunSnowRules.PackGunAfterLoad(p, 0f);
            float dtH = RunSnowRules.FramesToHours(framesPerPass);
            for (int pass = 1; pass < 200000; pass++)
            {
                RunSnowRules.StepTrackPack(ref t, p, 1000f, 1000f, airC, 1000f, 0f, true, true, dtH);
                if (t.ShareAtOrAbove(Bar) >= RunSnowRules.OpenAtShare)
                    return pass * dtH;
            }
            return float.PositiveInfinity;
        }

        public static void Run(Func<bool, string, string, bool> check)
        {
            // SS1
            check(RunSnowRules.StepScale(64u) == 1f && RunSnowRules.StepScale(32u) == 1f, W,
                  "SS1a one window at the speed-1 cadence (64 frames) and on the first pass of a load (the injection passes 64)");
            check(RunSnowRules.StepScale(FramesPerPass(8, 0.5f)) == 3.75f && RunSnowRules.StepScale(FramesPerPass(8, 0.8f)) == 6f, W,
                  "SS1b speed 8: 240 frames a pass = 3.75 windows, 384 at 0.8 s a pass = 6 (no longer capped at 4)");
            check(RunSnowRules.MaxStepScale == 1024f && RunSnowRules.StepScale(uint.MaxValue) == 1024f &&
                  RunSnowRules.StepScale(1024u * 64u) == 1024f, W,
                  "SS1c the only cap is the track's: MaxPackStepHours (6 game hours = 1024 windows), for a jump in the frame counter");
            check(FramesPerPass(1, 0.5f) == 64u && FramesPerPass(2, 0.5f) == 64u && FramesPerPass(8, 0.5f) == 240u && FramesPerPass(8, 0.8f) == 384u,
                  W, "SS1d the cadence model: 64 / 64 / 240 / 384 frames a pass");

            // SS2: the cause, with the old cap.
            float trackSlow8 = TrackOpensAtH(FramesPerPass(8, 0.8f), -5f);
            float oldRunSlow8 = RunOpensAtH(FramesPerPass(8, 0.8f), -5f, 4f);
            Console.WriteLine($"INFO  SnowSpeed: the pre-round-2 cap at speed 8, 384 frames a pass: run opens {oldRunSlow8:F3} h, track {trackSlow8:F3} h");
            check(oldRunSlow8 > 1.4f * trackSlow8, W,
                  $"SS2 the pre-round-2 cap: at speed 8 and 0.8 s a pass a run opened at {oldRunSlow8:F3} h, the track at {trackSlow8:F3} h");

            // SS3: the fix at speeds 1, 2 and 8, at full chill (-5 C) and in marginal air (+1.3 C: chill 0.5, the game melting).
            foreach (float air in new[] { -5f, -0.2f })
            {
                float run1 = RunOpensAtH(FramesPerPass(1, 0.5f), air, RunSnowRules.MaxStepScale);
                foreach (var (speed, interval) in new[] { (1, 0.5f), (2, 0.5f), (8, 0.5f), (8, 0.8f) })
                {
                    uint f = FramesPerPass(speed, interval);
                    float run = RunOpensAtH(f, air, RunSnowRules.MaxStepScale);
                    float track = TrackOpensAtH(f, air);
                    Console.WriteLine($"INFO  SnowSpeed: air {air} C, speed {speed}, {f} frames a pass: run opens {run:F3} h, track {track:F3} h");
                    // The run opens when 60 % of its texels reach the bar: the 40th-percentile texel rate (about 1.01) against the track's
                    // mean rate (1.125), a few game minutes later. 0.1 h = 6 game minutes.
                    check(run >= track - 0.02f && run - track <= 0.1f, W,
                          $"SS3a air {air} C, speed {speed} at {interval:F1} s a pass: run opens at {run:F3} h, track at {track:F3} h");
                    // and the same game time at every speed (one pass of quantization: 384 frames = 0.035 h)
                    check(Math.Abs(run - run1) <= 0.04f, W,
                          $"SS3b air {air} C, speed {speed} at {interval:F1} s a pass: run opens at {run:F3} h, at speed 1 {run1:F3} h");
                }
            }

            // SS4: no catch-up burst. A pause adds no frames: the next pass counts only the frames it ran (64 at speed 1).
            float d0 = 0.5f;
            float afterPause = RunSnowRules.PackRaiseTexelM(d0, RunSnowRules.GunStopDepthM, 1f, RunSnowRules.MeanRampRate, RunSnowRules.StepScale(64u));
            float oneWindow = RunSnowRules.RampPerTopupM * RunSnowRules.MeanRampRate * (RunSnowRules.RampChillFloor + RunSnowRules.RampChillGain);
            check(Math.Abs(afterPause - d0 - oneWindow) < 1e-5f, W,
                  $"SS4a after a pause the pass raises one window ({afterPause - d0:F5} m, expected {oneWindow:F5} m)");
            // A load: the injection's first pass passes 64 frames whatever the save's frame counter, and the track's first pass has dt 0.
            var p0 = RunSnowRules.PackParams.Default;
            var tl = default(RunSnowRules.TrackMadeSnow);
            tl.C0 = 0.5f;
            tl.Gun = RunSnowRules.PackGunAfterLoad(p0, 0.5f);
            RunSnowRules.StepTrackPack(ref tl, p0, 1000f, 1000f, -5f, 1000f, 0f, true, true, 0f);
            check(tl.C0 == 0.5f && Math.Abs(afterPause - d0 - oneWindow) < 1e-5f, W,
                  "SS4b the first pass of a load: one window on a run texel, nothing on a track cell (dt 0)");

            // SS5: the texels a run's share counts. The round 2 game turn read Upper Hidden Couloir at -5 C: 22 full texels, 59 fringe, 31
            // dropped by the edge noise (no target, never raised). Counted at their depth (0 m on made snow), the dropped ones capped the
            // share at 22 / 53 = 42 %, under OpenAtShare, so the run could never open on made snow. They now count only for a run with no
            // other measured texel.
            check(!RunSnowRules.CountDroppedTexels(22.0) && !RunSnowRules.CountDroppedTexels(RunSnowRules.MinFootprintTexels), W,
                  "SS5a a run with measured full texels leaves its noise-dropped texels out of its share and mean");
            check(RunSnowRules.CountDroppedTexels(0.0) && RunSnowRules.CountDroppedTexels(0.4) && RunSnowRules.CountDroppedTexels(double.NaN), W,
                  "SS5b a spineless run (fringe and dropped texels only, Moonbeam: 11 + 9) keeps its dropped texels: it stays measured, " +
                  "never falls to the day-mean estimate, and never opens bare");
            check(22f / 53f < RunSnowRules.OpenAtShare, W,
                  "SS5c the cause: with the dropped texels counted, the Couloir's share topped out at 22 / 53 under the open share");

            // SS6: the run's mean rate. The merge's noise has a coarse octave over 8 x 8 texel cells, so a narrow run sits on one or two cells
            // and its mean rate is that cell's, not the mean (the round 2 game turn: the Couloir's full texels built at about 0.83 of the
            // mean rate). RunRateScale brings each run's mean to MeanRampRate and keeps the texture inside it.
            check(RunSnowRules.RunRateScale(0.0, 0) == 1f && RunSnowRules.RunRateScale(double.NaN, 4) == 1f &&
                  Math.Abs(RunSnowRules.RunRateScale(50.0, 100) - 1f) < 1e-6f, W,
                  "SS6a the rate scale: 1 with no full texel, 1 at the mean noise (0.5)");
            bool meanOk = true;
            foreach (double m in new[] { 0.0, 0.1, 0.3, 0.7, 1.0 })
                meanOk &= Math.Abs((RunSnowRules.RampRateMin + RunSnowRules.RampRateSpan * (float)m) * RunSnowRules.RunRateScale(m * 40, 40)
                                   - RunSnowRules.MeanRampRate) < 1e-5f;
            check(meanOk, W, "SS6b a run's scaled mean rate is MeanRampRate at any mean noise 0..1");
            float trackSlow = TrackOpensAtH(FramesPerPass(1, 0.5f), -5f);
            foreach (float coarse in new[] { 0.1f, 0.9f })
            {
                // one coarse cell: the fine octave alone varies across the run's texels
                var raw = new List<float>();
                double noiseSum = 0.0;
                for (int j = 0; j < 40; j++)
                {
                    float noise = 0.65f * coarse + 0.35f * (j + 0.5f) / 40f;
                    noiseSum += noise;
                    raw.Add(RunSnowRules.RampRateMin + RunSnowRules.RampRateSpan * noise);
                }
                float k = RunSnowRules.RunRateScale(noiseSum, raw.Count);
                var scaled = raw.ConvertAll(r => r * k);
                float before = RunOpensAtH(FramesPerPass(1, 0.5f), -5f, RunSnowRules.MaxStepScale, raw);
                float after = RunOpensAtH(FramesPerPass(1, 0.5f), -5f, RunSnowRules.MaxStepScale, scaled);
                Console.WriteLine($"INFO  SnowSpeed: a run on one coarse noise cell {coarse:F1}: opens {before:F3} h unscaled, {after:F3} h scaled " +
                                  $"(x{k:F3}), track {trackSlow:F3} h");
                check(after >= trackSlow - 0.02f && after - trackSlow <= 0.06f, W,
                      $"SS6c a run on one coarse cell {coarse:F1} opens with the track: {after:F3} h (unscaled {before:F3} h), track {trackSlow:F3} h");
            }
        }
    }
}
