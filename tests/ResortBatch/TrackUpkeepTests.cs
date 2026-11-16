// A cat track's upkeep: grooming on its plan area while its guns can run and are on, snowmaking on the share of it the guns make snow on now
// (RunSnowRules.TrackMadeSnow.MakingShare, T222: a run bills the texels its guns raise; SkiRunUpkeepRates.TrackUpkeep). Groups:
// - TU1: StepTrackPack's MakingShare: the cells where the guns run and the air is cold enough, never on a pack at the base.
// - TU3: TrackUpkeep by feature, and the same hectares as SnowGunPlumeRules.TrackBilledHa.
// - TU4: a cold month: the pre-T220 rule billed the whole month at the snowmaking rate, the pack rule the build to the 2.0 m base.

using System;
using SkiResortMod.Systems;

namespace ResortBatch
{
    public static class TrackUpkeepTests
    {
        private const string W = "TrackUpkeep";

        public static void Run(Func<bool, string, string, bool> check)
        {
            RunSnowRules.PackParams p = RunSnowRules.PackParams.Default;
            const float baseH = 1000f, lapse = 0.0065f, lo = 1000f, hi = 1300f;

            // TU1: 4.3 C at the base puts the gun gate at 1153.8 m: the top 4 of 8 cells. Bare: the guns make snow there, 50 %.
            var t = new RunSnowRules.TrackMadeSnow();
            RunSnowRules.StepTrackPack(ref t, p, lo, hi, 4.3f, baseH, lapse, true, true, 0.1f);
            check(t.MakingShare == 0.5f && RunSnowRules.TrackGunsBuilding(t), W, "TU1 half the vertical cold enough: making on " + t.MakingShare);
            var atBase = RunSnowRules.TrackFromSave(1f, RunSnowRules.GunStopDepthM);
            RunSnowRules.StepTrackPack(ref atBase, p, lo, hi, -5f, baseH, lapse, true, true, 0.1f);
            check(atBase.MakingShare == 0f && !RunSnowRules.TrackGunsBuilding(atBase), W, "TU1b a pack at the 2.0 m base: no making");
            var off = new RunSnowRules.TrackMadeSnow();
            RunSnowRules.StepTrackPack(ref off, p, lo, hi, -5f, baseH, lapse, false, true, 0.1f);
            check(off.MakingShare == 0f && off.GunReach == 1f, W, "TU1c guns Off: no making, the reach stays");
            var warm = new RunSnowRules.TrackMadeSnow();
            RunSnowRules.StepTrackPack(ref warm, p, lo, hi, 12f, baseH, lapse, true, true, 0.1f);
            check(warm.MakingShare == 0f && warm.GunReach == 0f, W, "TU1d too warm: no making, no reach");

            // TU3
            RunFeatures track = RunFeatures.FromKind((int)RunKind.CatTrack);
            RunFeatures litTrack = RunFeatures.FromKind((int)RunKind.CatTrack, true, true);
            RunUpkeep idle = SkiRunUpkeepRates.TrackUpkeep(track, 38000f, true, true, 0f);
            check(Math.Abs(idle.GroomedHa - 3.8f) < 1e-4f && Math.Abs(idle.Grooming - 3.8f * SkiRunUpkeepRates.GroomedPerHaMonth) < 0.01f &&
                  idle.Snowmaking == 0f && idle.Lights == 0f, W,
                  "TU3 3.8 ha track at depth: grooming only (" + idle.Total + " a month)");
            RunUpkeep building = SkiRunUpkeepRates.TrackUpkeep(track, 38000f, true, true, 1f);
            check(Math.Abs(building.Total - 3.8f * (SkiRunUpkeepRates.GroomedPerHaMonth + SkiRunUpkeepRates.SnowmakingStepPerHaMonth)) < 0.01f, W,
                  "TU3b while its guns make snow on all of it: grooming + snowmaking on 3.8 ha (" + building.Total + ")");
            RunUpkeep halfMaking = SkiRunUpkeepRates.TrackUpkeep(track, 38000f, true, true, 0.5f);
            check(Math.Abs(halfMaking.SnowmakingHa - 1.9f) < 1e-4f && Math.Abs(halfMaking.GroomedHa - 3.8f) < 1e-4f &&
                  SkiRunUpkeepRates.TrackUpkeep(track, 38000f, true, true, float.NaN).SnowmakingHa == 0f &&
                  Math.Abs(SkiRunUpkeepRates.TrackUpkeep(track, 38000f, true, true, 3f).SnowmakingHa - 3.8f) < 1e-4f, W,
                  "TU3g making on half: snowmaking bills 1.9 ha, grooming 3.8 ha; NaN bills none, over 1 clamps");
            check(SkiRunUpkeepRates.TrackUpkeep(track, 38000f, true, false, 1f).Total == 0f &&
                  SkiRunUpkeepRates.TrackUpkeep(track, 38000f, false, true, 1f).Total == 0f, W,
                  "TU3c guns Off or out of season: nothing (DECISIONS 563)");
            RunUpkeep lit = SkiRunUpkeepRates.TrackUpkeep(litTrack, 38000f, true, false, 0f);
            check(lit.Grooming == 0f && Math.Abs(lit.Lights - 3.8f * SkiRunUpkeepRates.LightsStepPerHaMonth) < 0.01f, W,
                  "TU3d a lit track with guns Off: its lights in season only (DECISIONS 579)");
            foreach (float area in new[] { 0f, -5f, 1f, 24000f, float.NaN, float.PositiveInfinity })
                foreach (bool can in new[] { false, true })
                    foreach (bool on in new[] { false, true })
                    {
                        float want = SnowGunPlumeRules.TrackBilledHa(area, can, on);
                        float got = SkiRunUpkeepRates.TrackUpkeep(track, area, can, on, 0f).GroomedHa;
                        check(got == want || (float.IsInfinity(area) && got >= 0f), W,
                              "TU3e grooming hectares = TrackBilledHa (area " + area + ", can " + can + ", on " + on + "): " + got + " vs " + want);
                    }
            check(SkiRunUpkeepRates.TrackUpkeep(RunFeatures.FromKind((int)RunKind.Snowmaking), 38000f, true, true, 1f).Total == 0f, W,
                  "TU3f a piste is not billed by the track rule");

            // TU4: one cold game month (one game day, 24 h) at -8 C, passes every 0.12 game h, guns on all month after a bare start. The
            // pre-T220 rule billed 3.8 ha x the snowmaking rate the whole month; the pack rule bills the build to the 2.0 m base (1.3 game h).
            var m = new RunSnowRules.TrackMadeSnow();
            const float dt = 0.12f;
            double snowHaHours = 0.0;
            float monthHours = 24f;
            for (float h = 0f; h < monthHours; h += dt)
            {
                RunSnowRules.StepTrackPack(ref m, p, lo, hi, -8f, baseH, lapse, true, true, dt);
                snowHaHours += SkiRunUpkeepRates.TrackUpkeep(track, 38000f, true, true, m.MakingShare).SnowmakingHa * dt;
            }
            float build = RunSnowRules.GunStopDepthM / RunSnowRules.PackBuildPerHourM(-20f, p.MarginC, RunSnowRules.MeanRampRate);
            double oldBill = 3.8 * (SkiRunUpkeepRates.GroomedPerHaMonth + SkiRunUpkeepRates.SnowmakingStepPerHaMonth);
            double newBill = 3.8 * SkiRunUpkeepRates.GroomedPerHaMonth + SkiRunUpkeepRates.SnowmakingStepPerHaMonth * snowHaHours / monthHours;
            double snowHours = snowHaHours / 3.8;
            check(snowHours >= build - dt && snowHours <= build + 2f * dt && newBill < 0.4 * oldBill, W,
                  "TU4 a cold month from bare: snowmaking " + snowHours.ToString("F2") + " game h (build " + build.ToString("F2") + " h); bill " +
                  newBill.ToString("F0") + " vs " + oldBill.ToString("F0") + " before T220");
        }
    }
}
