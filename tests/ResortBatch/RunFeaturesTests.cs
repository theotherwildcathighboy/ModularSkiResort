// Run types R1 (DECISIONS 576): the golden parity table and the RunFeatures model. GOLDEN is every per-kind rule's answer for the five
// kinds and two unknown values. It is the dump of the kind-based code BEFORE the rules moved onto RunFeatures (origin/main 7db7b63),
// pasted here as a literal. The parity shim must reproduce it line for line. Set RESORTBATCH_DUMP_GOLDEN=<file> to write the current
// dump. Use it to diff against the literal when a rule changes on purpose. The literal then changes in the same commit, with a ledger
// entry. The feature-level checks after it pin the model itself.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SkiResortMod.Systems;
using Score = SkiResortMod.Systems.SkiResortScore;

namespace ResortBatch
{
    public static class RunFeaturesTests
    {
        private static Func<bool, string, string, bool> s_Check;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            Golden();
            Model();
            Steps();
            FeatureOnly();
            s_Check = null;
        }

        /// <summary>FromKind, Of's normalization, Unknown, equality.</summary>
        private static void Model()
        {
            const string W = "features/model";
            string[] want = { "piste+groomed", "piste+groomed+snowmaking", "piste+groomed+snowmaking+lights", "piste",
                              "cat track+groomed+snowmaking", "unknown", "unknown" };
            for (int i = 0; i < s_Kinds.Length; i++)
                Check(RunFeatures.FromKind(s_Kinds[i]).ToString() == want[i], W,
                      "kind " + s_Kinds[i] + " -> " + RunFeatures.FromKind(s_Kinds[i]) + ", want " + want[i]);
            Check(RunFeatures.FromKind(-1).Equals(RunFeatures.Unknown) && RunFeatures.Unknown.Equals(default(RunFeatures)), W, "-1 and default are Unknown");
            var u = RunFeatures.Unknown;
            Check(!u.Known && !u.Groomed && !u.Snowmaking && !u.Lights && !u.IsPiste && !u.IsCatTrack && !u.IsNatural, W, "Unknown has no feature and no base");
            // The switches: gunsOn drops snowmaking, lit lights a cat track only.
            Check(RunFeatures.FromKind((int)RunKind.Night, false).ToString() == "piste+groomed+lights", W, "night, guns off: " + RunFeatures.FromKind(2, false));
            Check(RunFeatures.FromKind((int)RunKind.CatTrack, false, true).ToString() == "cat track+groomed+lights", W,
                  "cat track, guns off, lit: " + RunFeatures.FromKind(4, false, true));
            Check(RunFeatures.FromKind((int)RunKind.Snowmaking, true, true).Equals(RunFeatures.FromKind((int)RunKind.Snowmaking)), W, "lit ignored on a run");
            Check(RunFeatures.FromKind((int)RunKind.Groomed, false).Equals(RunFeatures.FromKind((int)RunKind.Groomed)), W, "gunsOn ignored on a groomed run");
            // Of: snowmaking and lights need groomed. A cat track is groomed.
            Check(RunFeatures.Of(RunBase.Piste, false, true, true).Equals(RunFeatures.FromKind((int)RunKind.Natural)), W, "ungroomed piste drops snowmaking and lights");
            Check(RunFeatures.Of(RunBase.CatTrack, false, false, false).Groomed, W, "a cat track is groomed");
            // Equality and hash: all 16 known combinations plus Unknown are distinct.
            var seen = new HashSet<RunFeatures>();
            int hashes = 0;
            var hashSet = new HashSet<int>();
            foreach (RunBase b in new[] { RunBase.Piste, RunBase.CatTrack })
                for (int m = 0; m < 8; m++)
                {
                    var f = RunFeatures.Of(b, (m & 1) != 0, (m & 2) != 0, (m & 4) != 0);
                    seen.Add(f);
                    if (hashSet.Add(f.GetHashCode()))
                        hashes++;
                }
            seen.Add(u);
            if (hashSet.Add(u.GetHashCode()))
                hashes++;
            // Normalized: piste 5 (natural, groomed x snowmaking x lights), cat track 4 (snowmaking x lights), Unknown 1.
            Check(seen.Count == 10 && hashes == 10, W, seen.Count + " distinct feature sets, " + hashes + " hashes, want 10 / 10");
        }

        /// <summary>The fee and upkeep steps sum to the kind totals.</summary>
        private static void Steps()
        {
            const string W = "features/steps";
            Check(SkiRunFees.SnowmakingStepBase == 2000 && SkiRunFees.SnowmakingStepPerHa == 3000 && SkiRunFees.LightsStepBase == 2000 &&
                  SkiRunFees.LightsStepPerHa == 3000, W, "fee steps " + SkiRunFees.SnowmakingStepBase + "/" + SkiRunFees.SnowmakingStepPerHa + " " +
                  SkiRunFees.LightsStepBase + "/" + SkiRunFees.LightsStepPerHa + ", want 2000/3000 2000/3000");
            float[] areas = { 0f, 1f, 4999f, 5000f, 3245f, 84347f, 2.5e7f, 1e12f };
            for (int m = 0; m < 4; m++)
            {
                bool snow = (m & 1) != 0, lights = (m & 2) != 0;
                var f = RunFeatures.Of(RunBase.Piste, true, snow, lights);
                int wantBase = SkiRunFees.GroomedBase + (snow ? SkiRunFees.SnowmakingStepBase : 0) + (lights ? SkiRunFees.LightsStepBase : 0);
                int wantPerHa = SkiRunFees.GroomedPerHa + (snow ? SkiRunFees.SnowmakingStepPerHa : 0) + (lights ? SkiRunFees.LightsStepPerHa : 0);
                Check(SkiRunFees.BaseFee(f) == wantBase && SkiRunFees.PerHaFee(f) == wantPerHa, W, f + ": " + SkiRunFees.BaseFee(f) + "/" + SkiRunFees.PerHaFee(f));
                int wantUpkeep = 150 + (snow ? 350 : 0) + (lights ? 300 : 0);
                Check(SkiRunUpkeepRates.PerHaMonth(f) == wantUpkeep, W, f + " upkeep " + SkiRunUpkeepRates.PerHaMonth(f) + ", want " + wantUpkeep);
            }
            foreach (float a in areas)
            {
                Check(SkiRunFees.Fee(RunFeatures.Of(RunBase.Piste, true, true, false), a) == SkiRunFees.Fee(SkiRunFees.KindSnowmaking, a) &&
                      SkiRunFees.Fee(RunFeatures.Of(RunBase.Piste, true, true, true), a) == SkiRunFees.Fee(SkiRunFees.KindNight, a), W,
                      "groomed + steps = snowmaking / night fee at " + F(a) + " m2");
            }
            // The upkeep totals per kind (SkiRunUpkeepSystem.CostPerHaMonth wraps PerHaMonth(FromKind)): the values before R1.
            int[] upkeep = { 150, 500, 800, 0, 500, 0, 0 };
            for (int i = 0; i < s_Kinds.Length; i++)
            {
                int got = SkiRunUpkeepRates.PerHaMonth(RunFeatures.FromKind(s_Kinds[i]));
                Check(got == upkeep[i], W, "kind " + s_Kinds[i] + " upkeep " + got + ", want " + upkeep[i]);
            }
            // A cat track pays no fee for its guns or lights.
            Check(SkiRunFees.Fee(RunFeatures.FromKind(SkiRunFees.KindCatTrack, false, true), 84347f) == SkiRunFees.Fee(SkiRunFees.KindGroomed, 84347f), W,
                  "a lit cat track pays the groomed fee");
        }

        /// <summary>Combinations no kind has: a groomed lit piste without snowmaking, a lit cat track, a night run with its guns off.</summary>
        private static void FeatureOnly()
        {
            const string W = "features/only";
            var litGroomed = RunFeatures.Of(RunBase.Piste, true, false, true);
            Check(Score.TypeFactorOf(litGroomed, Score.BandState.Rated, Score.BandBlue) == Score.NightFactor && Score.CountsAsNight(litGroomed), W,
                  "a lit groomed piste is a night run in the score");
            Check(RunPropDensityRules.PropClassAllowed(litGroomed, RunPropDensityRules.ClassMast) &&
                  !RunPropDensityRules.PropClassAllowed(litGroomed, RunPropDensityRules.ClassGun) &&
                  !RunPropDensityRules.PropClassAllowed(litGroomed, RunPropDensityRules.ClassHydrant), W, "a lit groomed piste stands masts only");
            Check(!RunSnowRules.GunsMakeSnow(litGroomed, true) && RunSnowRules.ThresholdC(litGroomed, 0f, 2f) == RunSnowRules.GroomedMarginC, W,
                  "a lit groomed piste has the groomed gate and no guns");
            var litTrack = RunFeatures.FromKind((int)RunKind.CatTrack, true, true);
            Check(!Score.CountsAsNight(litTrack) && Score.TypeFactorOf(litTrack, Score.BandState.Rated, Score.BandGreen) == Score.CatTrackFactor &&
                  Score.SkiersFactorOf(litTrack) == Score.CatTrackSkiersFactor, W, "a lit cat track scores as a cat track");
            var nightOff = RunFeatures.FromKind((int)RunKind.Night, false);
            Check(!RunSnowRules.GunsMakeSnow(nightOff, true) && RunSnowRules.EstimateThresholdC(nightOff, 0f, 2f, true) == 0f &&
                  Score.CountsAsNight(nightOff), W, "a night run with its guns off: no snow, freezing, still a night run");
            Check(RunKindRules.HasSnowInjection(nightOff) && !RunKindRules.HasSnowInjection(litTrack) &&
                  !RunKindRules.HasSnowInjection(RunFeatures.FromKind((int)RunKind.Natural)), W, "injection: groomed pistes only");
            Check(PisteMapRules.IsDashed(RunFeatures.FromKind((int)RunKind.Natural)) && !PisteMapRules.IsDashed(RunFeatures.Unknown) &&
                  GroomerRules.SourceFor(litTrack) != null && GroomerRules.SourceFor(litGroomed) == null, W, "map dash and groomer by feature");
        }

        private static bool Check(bool cond, string where, string msg) => s_Check(cond, where, msg);

        private static readonly int[] s_Kinds = { 0, 1, 2, 3, 4, 5, 255 };

        private static string F(double v) => double.IsNaN(v) ? "NaN" : v.ToString("R", CultureInfo.InvariantCulture);

        /// <summary>Every per-kind rule, one line per call.</summary>
        internal static string Dump()
        {
            var sb = new StringBuilder();
            float[] areas = { 0f, 3245f, 84347f, float.NaN, -5f, 1e12f };
            foreach (int k in s_Kinds)
            {
                var rk = (RunKind)(byte)k;
                sb.Append("kind ").Append(k).Append(": counts ").Append(RunKindRules.CountsTowardResort(k))
                  .Append(" link ").Append(RunKindRules.IsCatTrackLink(k)).Append(" inject ").Append(RunKindRules.HasSnowInjection(k)).Append('\n');
                sb.Append("  fee base ").Append(SkiRunFees.BaseFee(k)).Append(" perHa ").Append(SkiRunFees.PerHaFee(k)).Append(" fees");
                foreach (float a in areas)
                    sb.Append(' ').Append(SkiRunFees.Fee(k, a));
                sb.Append('\n');
                sb.Append("  threshold ").Append(F(RunSnowRules.ThresholdC(rk, 0f, 2f))).Append(' ').Append(F(RunSnowRules.ThresholdC(rk, -1f, 1.5f)))
                  .Append(" gunKind ").Append(RunSnowRules.IsGunKind(rk)).Append(" makes");
                foreach (bool season in new[] { false, true })
                    foreach (bool on in new[] { false, true })
                        sb.Append(' ').Append(RunSnowRules.GunsMakeSnow(rk, season, on) ? 1 : 0)
                          .Append('/').Append(F(RunSnowRules.EstimateThresholdC(rk, 0f, 2f, season, on)));
                sb.Append(" bar");
                for (int band = 0; band <= 4; band++)
                    sb.Append(' ').Append(F(RunSnowRules.SkiableBarNorm(rk, band)));
                sb.Append('\n');
                sb.Append("  type");
                foreach (Score.BandState st in new[] { Score.BandState.Rated, Score.BandState.Unrateable, Score.BandState.Pending })
                    for (byte band = 0; band <= 4; band++)
                        sb.Append(' ').Append(F(Score.TypeFactorOf((byte)k, st, band)));
                sb.Append(" skiers");
                for (byte band = 0; band <= 4; band++)
                {
                    var run = new Score.RunInput { Kind = (byte)k, State = Score.BandState.Rated, Band = band, AreaM2 = 10000.0 };
                    var part = new Score.RunPart { Counted = true, ServedFactor = 1.0, OpenFactor = 1.0 };
                    sb.Append(' ').Append(F(Score.RunSkiersPerDay(run, part)));
                }
                sb.Append(" night ").Append(NightCounted((byte)k)).Append('\n');
                sb.Append("  props");
                foreach (bool lit in new[] { false, true })
                    for (byte cls = 0; cls <= 4; cls++)
                        sb.Append(' ').Append(RunPropDensityRules.PropClassAllowed(k, cls, lit) ? 1 : 0);
                sb.Append(" spacing ").Append(RunPropPlacerRules.DefaultGunSpacing(k)).Append('/').Append(RunPropPlacerRules.DefaultLightSpacing(k))
                  .Append(" groomer ").Append(GroomerRules.SourceFor(k) != null).Append(" dashed ").Append(PisteMapRules.IsDashed(k)).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>NightRunsCounted of one served, open, rated green 1 ha run of this kind (Compute's night count, the ski day's
        /// close hour input).</summary>
        private static int NightCounted(byte kind)
        {
            var runs = new List<Score.RunInput>
            {
                new Score.RunInput { Kind = kind, State = Score.BandState.Rated, Band = Score.BandGreen, AreaM2 = 10000.0, Served = true, OpenFactor = 1.0 },
            };
            var area = new Score.AreaInput
            {
                ActiveLodges = 1,
                Efficiency = 1.0,
                Settings = new Score.ScoreSettings { K = 100, BackcountryPercent = 0, SnowGating = true },
            };
            return Score.Compute(area, runs, new List<Score.LiftInput>(), null).NightRunsCounted;
        }

        private const string GOLDEN =
            "kind 0: counts True link False inject True\n" +
            "  fee base 2000 perHa 1000 fees 2000 2320 10430 2000 2000 2002000\n" +
            "  threshold 0 -1 gunKind False makes 0/0 0/0 0/0 0/0 bar 0.11375000327825546 0.11375000327825546 0.11375000327825546 0.11375000327825546 0.11375000327825546\n" +
            "  type 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 skiers 0 45 25 15 7.5 night 0\n" +
            "  props 0 0 0 0 0 0 0 0 0 0 spacing 0/0 groomer False dashed False\n" +
            "kind 1: counts True link False inject True\n" +
            "  fee base 4000 perHa 4000 fees 4000 5300 37740 4000 4000 8004000\n" +
            "  threshold 2 0.5 gunKind True makes 0/0 0/0 0/0 1/2 bar 0.11375000327825546 0.11375000327825546 0.11375000327825546 0.11375000327825546 0.11375000327825546\n" +
            "  type 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 skiers 0 45 25 15 7.5 night 0\n" +
            "  props 0 1 1 0 0 0 1 1 0 0 spacing 50/0 groomer False dashed False\n" +
            "kind 2: counts True link False inject True\n" +
            "  fee base 6000 perHa 7000 fees 6000 8270 65040 6000 6000 14006000\n" +
            "  threshold 2 0.5 gunKind True makes 0/0 0/0 0/0 1/2 bar 0.11375000327825546 0.11375000327825546 0.11375000327825546 0.11375000327825546 0.11375000327825546\n" +
            "  type 1.25 1.25 1.25 1.25 1.25 1.25 1.25 1.25 1.25 1.25 1.25 1.25 1.25 1.25 1.25 skiers 0 45 25 15 7.5 night 1\n" +
            "  props 0 1 1 1 0 0 1 1 1 0 spacing 90/90 groomer False dashed False\n" +
            "kind 3: counts True link False inject False\n" +
            "  fee base 500 perHa 250 fees 500 580 2610 500 500 500500\n" +
            "  threshold 0 -1 gunKind False makes 0/0 0/0 0/0 0/0 bar 0.11375000327825546 0.11375000327825546 0.11375000327825546 0.11375000327825546 0.11375000327825546\n" +
            "  type 0.5 0.5 0.75 1 1 0.5 0.5 0.5 0.5 0.5 0.5 0.5 0.5 0.5 0.5 skiers 0 22.5 12.5 7.5 3.75 night 0\n" +
            "  props 0 0 0 0 0 0 0 0 0 0 spacing 0/0 groomer False dashed True\n" +
            "kind 4: counts True link True inject False\n" +
            "  fee base 2000 perHa 1000 fees 2000 2320 10430 2000 2000 2002000\n" +
            "  threshold 2 0.5 gunKind True makes 0/0 0/0 0/0 1/2 bar 0.11375000327825546 0.11375000327825546 0.11375000327825546 0.11375000327825546 0.11375000327825546\n" +
            "  type 0 0.25 0 0 0 0 0 0 0 0 0 0 0 0 0 skiers 0 11.25 6.25 3.75 1.875 night 0\n" +
            "  props 0 1 1 0 0 0 1 1 1 0 spacing 60/0 groomer True dashed False\n" +
            "kind 5: counts False link False inject False\n" +
            "  fee base -1 perHa -1 fees -1 -1 -1 -1 -1 -1\n" +
            "  threshold 0 -1 gunKind False makes 0/0 0/0 0/0 0/0 bar 0.11375000327825546 0.11375000327825546 0.11375000327825546 0.11375000327825546 0.11375000327825546\n" +
            "  type 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 skiers 0 45 25 15 7.5 night 0\n" +
            "  props 0 0 0 0 0 0 0 0 0 0 spacing 0/0 groomer False dashed False\n" +
            "kind 255: counts False link False inject False\n" +
            "  fee base -1 perHa -1 fees -1 -1 -1 -1 -1 -1\n" +
            "  threshold 0 -1 gunKind False makes 0/0 0/0 0/0 0/0 bar 0.11375000327825546 0.11375000327825546 0.11375000327825546 0.11375000327825546 0.11375000327825546\n" +
            "  type 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 skiers 0 45 25 15 7.5 night 0\n" +
            "  props 0 0 0 0 0 0 0 0 0 0 spacing 0/0 groomer False dashed False\n";

        private static void Golden()
        {
            const string W = "features/golden";
            string dump = Dump();
            string path = Environment.GetEnvironmentVariable("RESORTBATCH_DUMP_GOLDEN");
            if (!string.IsNullOrEmpty(path))
                System.IO.File.WriteAllText(path, dump);
            string[] want = GOLDEN.Split('\n'), got = dump.Split('\n');
            int bad = 0;
            for (int i = 0; i < Math.Max(want.Length, got.Length); i++)
            {
                string a = i < want.Length ? want[i] : "<none>", b = i < got.Length ? got[i] : "<none>";
                if (a != b && bad++ < 8)
                    Check(false, W, "line " + i + ": golden '" + a + "' now '" + b + "'");
            }
            Check(bad == 0, W, want.Length + " golden line(s), " + bad + " differ");
        }
    }
}
