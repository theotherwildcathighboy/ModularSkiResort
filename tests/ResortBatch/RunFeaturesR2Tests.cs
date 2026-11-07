// Run types R2 (DECISIONS 577): features from the sliders and the Groomed toggle. Checks:
// - FromSpacings at the kind defaults equals FromKind (the R1 golden still holds for every placed preset).
// - SnowKind and ScoreKind give the same answers as the features themselves. They are the kind that the snow and score code receive.
// - The paid-once billing: a random knob-fiddling sequence pays exactly the fee of the most the run ever had. It never refunds.
//   Re-enabling is free. Spacing changes are free.
// - Upkeep is billed per feature only while the feature is on.

using System;
using SkiResortMod.Systems;
using Score = SkiResortMod.Systems.SkiResortScore;

namespace ResortBatch
{
    public static class RunFeaturesR2Tests
    {
        private static Func<bool, string, string, bool> s_Check;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            Spacings();
            Kinds();
            KnobFiddling();
            s_Check = null;
        }

        /// <summary>FromSpacings: the kind defaults reproduce FromKind. Spacings switch guns and lights on a groomed run only.</summary>
        private static void Spacings()
        {
            const string W = "features/spacings";
            for (int k = 0; k <= 4; k++)
            {
                RunFeatures a = RunFeatures.FromSpacings(k, RunPropPlacerRules.DefaultGunSpacing(k), RunPropPlacerRules.DefaultLightSpacing(k));
                RunFeatures b = RunFeatures.FromKind(k, RunPropPlacerRules.GunsOn(RunPropPlacerRules.DefaultGunSpacing(k)),
                                                     RunPropPlacerRules.DefaultLightSpacing(k) > RunPropPlacerRules.SpacingOff);
                Check(a.Equals(b), W, "kind " + k + " defaults: " + a + " vs FromKind " + b);
            }
            Check(RunFeatures.FromSpacings(5, 60, 60).Equals(RunFeatures.Unknown), W, "an unknown kind stays unknown");
            for (int k = 0; k <= 2; k++)   // every groomed preset is the same run with other sliders
            {
                Check(RunFeatures.FromSpacings(k, 0, 0).ToString() == "piste+groomed" &&
                      RunFeatures.FromSpacings(k, 60, 0).ToString() == "piste+groomed+snowmaking" &&
                      RunFeatures.FromSpacings(k, 0, 60).ToString() == "piste+groomed+lights" &&
                      RunFeatures.FromSpacings(k, 60, 60).ToString() == "piste+groomed+snowmaking+lights", W, "preset " + k + " follows its sliders");
            }
            Check(RunFeatures.FromSpacings((int)RunKind.Natural, 60, 60).ToString() == "piste", W, "a natural run ignores its sliders (lights need Groomed)");
            Check(RunPropPlacerRules.CarriesGuns((int)RunKind.Groomed) && RunPropPlacerRules.CarriesLights((int)RunKind.Groomed) &&
                  !RunPropPlacerRules.CarriesGuns((int)RunKind.Natural) && !RunPropPlacerRules.CarriesLights((int)RunKind.Natural), W,
                  "groomed runs carry both sliders, natural runs none");
        }

        /// <summary>The kind handed to the snow code (SnowKind) and to the score (ScoreKind) answers like the features.</summary>
        private static void Kinds()
        {
            const string W = "features/kinds";
            for (int b = 0; b < 2; b++)
                for (int m = 0; m < 8; m++)
                {
                    RunFeatures f = RunFeatures.Of(b == 0 ? RunBase.Piste : RunBase.CatTrack, (m & 1) != 0, (m & 2) != 0, (m & 4) != 0);
                    RunFeatures s = RunFeatures.FromKind((int)f.SnowKind, f.Snowmaking);
                    Check(RunSnowRules.GunsMakeSnow(s, true) == RunSnowRules.GunsMakeSnow(f, true) &&
                          RunSnowRules.ThresholdC(s, 0f, 2f) == RunSnowRules.ThresholdC(f, 0f, 2f) &&
                          RunKindRules.HasSnowInjection(s) == RunKindRules.HasSnowInjection(f), W, f + " snow as kind " + (int)f.SnowKind);
                    RunFeatures c = RunFeatures.FromKind(f.ScoreKind, true, f.Lights);
                    Check(Score.TypeFactorOf(c, Score.BandState.Rated, Score.BandBlue) == Score.TypeFactorOf(f, Score.BandState.Rated, Score.BandBlue) &&
                          Score.CountsAsNight(c) == Score.CountsAsNight(f) && Score.SkiersFactorOf(c) == Score.SkiersFactorOf(f), W,
                          f + " score as kind " + f.ScoreKind);
                }
            Check((int)RunFeatures.Unknown.SnowKind == 255 && RunFeatures.Unknown.ScoreKind == 255, W, "unknown reads 255");
            for (int k = 0; k <= 4; k++)
                Check((int)RunFeatures.FromKind(k).SnowKind == k, W, "kind " + k + " is its own snow kind");
        }

        /// <summary>Random toggle and slider sequences on one bound run. The one-time total is the fee of every step it ever had.
        /// Each charge is >= 0. A step paid once is free again. A slider move that keeps a feature on costs nothing.
        /// Upkeep integrates 150 groomed + 350 snowmaking + 300 lights per ha-month over the time each is on.</summary>
        private static void KnobFiddling()
        {
            const string W = "features/knobs";
            var rng = new Random(577);
            int[] spacings = { RunPropPlacerRules.SpacingOff, 30, 45, 60, 90, 120 };
            for (int run = 0; run < 400; run++)
            {
                float area = run % 7 == 0 ? 0f : (float)(rng.NextDouble() * 200000.0);
                bool groomed = rng.Next(2) == 0;
                int gun = spacings[rng.Next(spacings.Length)], light = spacings[rng.Next(spacings.Length)];
                RunFeatures f = Features(groomed, gun, light);
                long total = SkiRunFees.Fee(f, area);   // the bind charge
                byte paid = SkiRunFees.StepsOf(f), ever = paid;
                double upkeep = 0.0, want = 0.0, ha = area / 10000.0;
                bool ok = true;
                string why = "";
                for (int step = 0; step < 60 && ok; step++)
                {
                    double months = rng.NextDouble();
                    upkeep += SkiRunUpkeepRates.PerHaMonth(f) * ha * months;
                    want += ((f.Groomed ? 150 : 0) + (f.Snowmaking ? 350 : 0) + (f.Lights ? 300 : 0)) * ha * months;
                    RunFeatures before = f;
                    switch (rng.Next(3))
                    {
                        case 0: groomed = !groomed; break;
                        case 1: gun = spacings[rng.Next(spacings.Length)]; break;
                        default: light = spacings[rng.Next(spacings.Length)]; break;
                    }
                    f = Features(groomed, gun, light);
                    int charge = SkiRunFees.StepCharge(f, paid, area, out byte nowPaid);
                    bool sameSteps = (SkiRunFees.StepsOf(f) & ~paid) == 0;
                    if (charge < 0 || (sameSteps && charge != 0) || (before.Equals(f) && charge != 0))
                    {
                        ok = false;
                        why = before + " -> " + f + " charged " + charge + " with paid " + paid;
                    }
                    total += charge;
                    paid = nowPaid;
                    ever |= SkiRunFees.StepsOf(f);
                }
                if (ok && paid != ever)
                {
                    ok = false;
                    why = "paid steps " + paid + ", ever " + ever;
                }
                long most = SkiRunFees.FeeOfSteps(RunFeatures.Of(RunBase.Piste, false, false, false), ever, area);
                if (ok && total != most)
                {
                    ok = false;
                    why = "one-time total " + total + ", fee of the most it ever had " + most;
                }
                if (ok && Math.Abs(upkeep - want) > 1e-6 * Math.Max(1.0, want))
                {
                    ok = false;
                    why = "upkeep " + upkeep + ", want " + want;
                }
                Check(ok, W, "run " + run + " (area " + area + "): " + (ok ? "ok" : why));
            }
            // The cat track: no step ever charges.
            RunFeatures track = RunFeatures.FromSpacings((int)RunKind.CatTrack, 60, 60);
            Check(SkiRunFees.StepCharge(track, 0, 84347f, out _) == 0 && SkiRunFees.StepsOf(track) == 0, W, "a cat track pays no step");
        }

        private static RunFeatures Features(bool groomed, int gun, int light) =>
            RunFeatures.FromSpacings(groomed ? (int)RunKind.Groomed : (int)RunKind.Natural, gun, light);

        private static bool Check(bool cond, string where, string msg) => s_Check(cond, where, msg);
    }
}
