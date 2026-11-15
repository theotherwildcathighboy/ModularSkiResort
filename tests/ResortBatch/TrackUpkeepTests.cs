// A cat track's upkeep: grooming on its plan area while its guns can run and are on, snowmaking only while its guns build made snow
// (RunSnowRules.StepTrackGunWork / TrackGunsWorking, SkiRunUpkeepRates.TrackUpkeep). Groups:
// - TU1: TrackBuildHours is a run's mean-texel build from bare to the gun stop depth.
// - TU2: StepTrackGunWork: a raise adds work, time spends it, natural snow and what was there do not count, no gun no work.
// - TU3: TrackUpkeep by feature, and the same hectares as SnowGunPlumeRules.TrackBilledHa.
// - TU4: a cold month: the old rule billed the whole month at the snowmaking rate, the new one about one game hour of it.

using System;
using SkiResortMod.Systems;

namespace ResortBatch
{
    public static class TrackUpkeepTests
    {
        private const string W = "TrackUpkeep";

        public static void Run(Func<bool, string, string, bool> check)
        {
            // TU1
            float mean = RunSnowRules.RampRateMin + 0.5f * RunSnowRules.RampRateSpan;
            float hand = RunSnowRules.GunStopDepthM /
                         (RunSnowRules.RampPerTopupM * mean * (RunSnowRules.RampChillFloor + 0.5f * RunSnowRules.RampChillGain)) *
                         RunSnowRules.TopupIntervalFrames / RunSnowRules.FramesPerGameHour;
            check(Math.Abs(RunSnowRules.TrackBuildHours - hand) < 1e-4f && RunSnowRules.TrackBuildHours > 0.9f && RunSnowRules.TrackBuildHours < 1.2f, W,
                  "TU1 a full build is about one game hour (" + RunSnowRules.TrackBuildHours + " h, by hand " + hand + ")");

            // TU2
            var t = new RunSnowRules.TrackMadeSnow { Share = 1f };
            RunSnowRules.StepTrackGunWork(ref t, 0f, 1f, float.NaN, 0.1f);
            check(Math.Abs(t.GunWorkHours - RunSnowRules.TrackBuildHours) < 1e-5f && RunSnowRules.TrackGunsWorking(t), W,
                  "TU2 a bare track raised to 100 %: one full build of work (" + t.GunWorkHours + " h)");
            RunSnowRules.StepTrackGunWork(ref t, 1f, 1f, float.NaN, 0.5f);
            check(Math.Abs(t.GunWorkHours - (RunSnowRules.TrackBuildHours - 0.5f)) < 1e-5f && RunSnowRules.TrackGunsWorking(t), W,
                  "TU2b half an hour later, nothing new raised: the work left drops by 0.5 h");
            RunSnowRules.StepTrackGunWork(ref t, 1f, 1f, float.NaN, 2f);
            check(t.GunWorkHours == 0f && !RunSnowRules.TrackGunsWorking(t), W, "TU2c the work is done: the guns stand at depth");

            var half = new RunSnowRules.TrackMadeSnow { Share = 0.8f };
            RunSnowRules.StepTrackGunWork(ref half, 0.3f, 0.8f, float.NaN, 0f);
            check(Math.Abs(half.GunWorkHours - 0.5f * RunSnowRules.TrackBuildHours) < 1e-5f, W,
                  "TU2d raised from 30 % to 80 %: half a build of work (" + half.GunWorkHours + " h)");
            var natural = new RunSnowRules.TrackMadeSnow { Share = 0.8f };
            RunSnowRules.StepTrackGunWork(ref natural, 0f, 0.8f, 0.6f, 0f);
            check(Math.Abs(natural.GunWorkHours - 0.2f * RunSnowRules.TrackBuildHours) < 1e-5f, W,
                  "TU2e measured natural snow covers 60 %: only the 20 % above it is the guns' work (" + natural.GunWorkHours + " h)");
            var deep = new RunSnowRules.TrackMadeSnow { Share = 0.8f };
            RunSnowRules.StepTrackGunWork(ref deep, 0f, 0.8f, 1f, 0f);
            check(deep.GunWorkHours == 0f, W, "TU2f natural snow already covers the guns' reach: no work");
            var stop = new RunSnowRules.TrackMadeSnow { Share = 1f, GunWorkHours = 0.7f };
            RunSnowRules.StepTrackGunWork(ref stop, 1f, 0f, float.NaN, 0f);
            check(stop.GunWorkHours == 0f, W, "TU2g guns make no snow now (out of season, Off, too warm): the work stops");
            var cap = new RunSnowRules.TrackMadeSnow { Share = 1f, GunWorkHours = RunSnowRules.TrackBuildHours };
            RunSnowRules.StepTrackGunWork(ref cap, 0f, 1f, float.NaN, 0f);
            check(Math.Abs(cap.GunWorkHours - RunSnowRules.TrackBuildHours) < 1e-5f, W, "TU2h the work left never exceeds one full build");
            var nan = new RunSnowRules.TrackMadeSnow { Share = float.NaN };
            RunSnowRules.StepTrackGunWork(ref nan, float.NaN, 1f, float.NaN, float.NaN);
            check(nan.GunWorkHours == 0f, W, "TU2i NaN share, before and dt: no work, no NaN");

            // TU3
            RunFeatures track = RunFeatures.FromKind((int)RunKind.CatTrack);
            RunFeatures litTrack = RunFeatures.FromKind((int)RunKind.CatTrack, true, true);
            RunUpkeep idle = SkiRunUpkeepRates.TrackUpkeep(track, 38000f, true, true, false);
            check(Math.Abs(idle.GroomedHa - 3.8f) < 1e-4f && Math.Abs(idle.Grooming - 3.8f * SkiRunUpkeepRates.GroomedPerHaMonth) < 0.01f &&
                  idle.Snowmaking == 0f && idle.Lights == 0f, W,
                  "TU3 3.8 ha track at depth: grooming only (" + idle.Total + " a month)");
            RunUpkeep building = SkiRunUpkeepRates.TrackUpkeep(track, 38000f, true, true, true);
            check(Math.Abs(building.Total - 3.8f * (SkiRunUpkeepRates.GroomedPerHaMonth + SkiRunUpkeepRates.SnowmakingStepPerHaMonth)) < 0.01f, W,
                  "TU3b while its guns build: grooming + snowmaking, the old whole-season rate (" + building.Total + ")");
            check(SkiRunUpkeepRates.TrackUpkeep(track, 38000f, true, false, true).Total == 0f &&
                  SkiRunUpkeepRates.TrackUpkeep(track, 38000f, false, true, true).Total == 0f, W,
                  "TU3c guns Off or out of season: nothing (DECISIONS 563)");
            RunUpkeep lit = SkiRunUpkeepRates.TrackUpkeep(litTrack, 38000f, true, false, false);
            check(lit.Grooming == 0f && Math.Abs(lit.Lights - 3.8f * SkiRunUpkeepRates.LightsStepPerHaMonth) < 0.01f, W,
                  "TU3d a lit track with guns Off: its lights in season only (DECISIONS 579)");
            foreach (float area in new[] { 0f, -5f, 1f, 24000f, float.NaN, float.PositiveInfinity })
                foreach (bool can in new[] { false, true })
                    foreach (bool on in new[] { false, true })
                    {
                        float want = SnowGunPlumeRules.TrackBilledHa(area, can, on);
                        float got = SkiRunUpkeepRates.TrackUpkeep(track, area, can, on, false).GroomedHa;
                        check(got == want || (float.IsInfinity(area) && got >= 0f), W,
                              "TU3e grooming hectares = TrackBilledHa (area " + area + ", can " + can + ", on " + on + "): " + got + " vs " + want);
                    }
            check(SkiRunUpkeepRates.TrackUpkeep(RunFeatures.FromKind((int)RunKind.Snowmaking), 38000f, true, true, true).Total == 0f, W,
                  "TU3f a piste is not billed by the track rule");

            // TU4: one cold game month (12 days a year: one game day, 24 h), passes every 0.12 game h, guns on all month after a bare
            // start. The old rule billed 3.8 ha x 500 the whole month; the new one grooming all month and snowmaking about one game hour.
            var m = new RunSnowRules.TrackMadeSnow();
            const float dt = 0.12f;
            double snowHours = 0.0;
            float monthHours = 24f;
            for (float h = 0f; h < monthHours; h += dt)
            {
                float before = m.Share;
                RunSnowRules.StepTrackMadeSnow(ref m, dt, 1f, 1f, -8f, false, out bool _);
                RunSnowRules.StepTrackGunWork(ref m, before, 1f, float.NaN, dt);
                if (RunSnowRules.TrackGunsWorking(m))
                    snowHours += dt;
            }
            double oldBill = 3.8 * (SkiRunUpkeepRates.GroomedPerHaMonth + SkiRunUpkeepRates.SnowmakingStepPerHaMonth);
            double newBill = 3.8 * SkiRunUpkeepRates.GroomedPerHaMonth + 3.8 * SkiRunUpkeepRates.SnowmakingStepPerHaMonth * snowHours / monthHours;
            check(snowHours >= RunSnowRules.TrackBuildHours - dt && snowHours <= RunSnowRules.TrackBuildHours + dt && newBill < 0.35 * oldBill, W,
                  "TU4 a cold month from bare: snowmaking " + snowHours.ToString("F2") + " game h; bill " + newBill.ToString("F0") + " vs " +
                  oldBill.ToString("F0") + " before");
        }
    }
}
