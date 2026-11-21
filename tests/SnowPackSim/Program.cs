// T222 season simulator. Run (from the ModularSkiResort root):
//   dotnet run --project tests/SnowPackSim/SnowPackSim.csproj -c Release -- <climate csv dir> <out dir> [melt list] [add]
// Prints a summary table and writes, per climate and melt rate, an
// hourly CSV (one row per game hour of the recorded season) for the charts.
//
// Model (every term is a stated assumption; the report lists them):
// - Air: the climate's hourly curve at the map's base height (vanilla ClimatePrefab.m_Temperature, one key per game hour), linearly
//   interpolated over 0.25 h substeps, lapsed by 0.01 C per meter (ClimateSystem.snowTemperatureHeightScale) at each cell's height.
// - Vanilla snow on a cell: + AddPerPrecipHourM x precipitation per hour at or below 0 C (the add form is compiled HLSL: CALIBRATED,
//   not measured), - melt x (T - 0) per hour above 0 C (the melt form: linear per the bridge r1d forced melt, the rate is the argument).
// - A surface is CELLS cells evenly from BottomM to TopM above the base height (a run's texels down its fall line, a track's vertical).
// - Open state: RunSnowRules.MeasuredShare / NextState over the cells (the share of cells at the bar).
// - Year: 12 days of 24 h (TimeSettingsPrefab). The record starts at the season rollover (0.6) after one warm-up year.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using SkiResortMod.Systems;

namespace SnowPackSim
{
    internal static class Program
    {
        private const int Cells = 15;
        private static readonly float BottomM = EnvF("SIM_BOTTOM_M", 300f), TopM = EnvF("SIM_TOP_M", 1000f);
        private const float LapsePerM = 0.01f;
        private static float EnvF(string name, float dflt) => float.TryParse(System.Environment.GetEnvironmentVariable(name), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : dflt;
        private static readonly float Dt = EnvF("SIM_DT_H", 0.25f);   // game hours per step; 0.00586 = one 64-frame top-up window
        private const int HoursPerYear = 288;
        private const float MarginC = RunSnowRules.DefaultSnowmakingMarginC;
        private static float s_AddPerPrecipHourM;
        private static float s_Melt;   // the melt's depth term (RunSnowRules.MeltDepthCoef, m^2/h/C); the floor is the game's

        /// <summary>The live pack parameters with stop depth <paramref name="stop"/> and this run's depth term.</summary>
        private static RunSnowRules.PackParams MeltParams(float stop)
        {
            RunSnowRules.PackParams p = RunSnowRules.PackParams.With(stop);
            p.MeltDepthCoef = s_Melt;
            return p;
        }

        private static float CellY(int i) => BottomM + (TopM - BottomM) * i / (Cells - 1);
        private static float Local(float tBase, float y) => tBase - LapsePerM * y;

        // ------------------------------------------------------------------ surfaces
        private abstract class Surface
        {
            public string Name;
            public RunSnowState State = RunSnowState.Unknown;
            public float[] D = new float[Cells];
            public bool GunsNow;
            public double GunHours, BuiltM, OpenHours;
            public float Peak, PeakTop;
            public abstract void Step(float date, float tBase, float precip, float[] nat);
            public virtual float MeanDepth()
            {
                double s = 0;
                foreach (var x in D) s += x;
                return (float)(s / Cells);
            }
            public virtual float CountedShare(RunSnowState prev)
            {
                int open = 0, keep = 0;
                foreach (var x in D)
                {
                    if (x >= RunSnowRules.SkiableDepthM) open++;
                    if (x >= RunSnowRules.KeepDepthM) keep++;
                }
                return RunSnowRules.MeasuredShare(prev, open / (float)Cells, keep / (float)Cells);
            }
            public void Label() => State = RunSnowRules.NextState(State, CountedShare(State));

            // T268: the same surface under the open rule by continuity (RunSnowRules.OpenShare over elevation bands). Cat tracks keep
            // the area rule (HasBands false). Each cell counts as BandCellWeight texels, so a band of one cell is still judged.
            public RunSnowState StateC = RunSnowState.Unknown;
            public double OpenHoursC;
            public virtual bool HasBands => true;
            public float CountedShareC(RunSnowState prev)
            {
                float area = CountedShare(prev);
                if (!HasBands) return area;
                float bar = RunSnowRules.KeepDepthM;   // the band share counts at the keep bar in every state (RunSnowRules T268)
                var foot = new double[ContinuityBands];
                var ski = new double[ContinuityBands];
                for (int i = 0; i < Cells; i++)
                {
                    int b = RunSnowRules.ContinuityBand(CellY(i), BottomM, TopM);
                    foot[b] += BandCellWeight;
                    if (D[i] >= bar) ski[b] += BandCellWeight;
                }
                float worst = RunSnowRules.WorstBandShare(foot, ski, 0, out _);
                return RunSnowRules.OpenShare(area, worst);
            }
            public void LabelC() => StateC = RunSnowRules.NextState(StateC, CountedShareC(StateC));
        }

        private const int ContinuityBands = RunSnowRules.ContinuityBands;
        private const float BandCellWeight = 4f;

        private static void Vanilla(float[] d, float tBase, float precip)
        {
            for (int i = 0; i < Cells; i++)
            {
                float t = Local(tBase, CellY(i));
                if (t <= 0f) d[i] += s_AddPerPrecipHourM * precip * Dt;
                else d[i] = Math.Max(0f, d[i] - VanillaMeltM(d[i], t));
            }
        }

        // SIM_MELT_Q=1 (T269 calibration): the snow map's melt in whole R16 quanta per 4-frame update, rounded up (a fit to the bridge melt
        // probe, the shader form is unverified): ceil(max(floor, coef x above / d) x dt / quantum) quanta per update, the depth first
        // rounded to R16. Use it with a step of whole updates (SIM_DT_H=0.00586, one 64-frame window). Off: the continuous law (PackMeltM).
        // Only snow map texels melt this way (natural snow and runs); the mod melts a cat track's cells with the continuous law.
        private static readonly bool s_MeltQ = System.Environment.GetEnvironmentVariable("SIM_MELT_Q") == "1";

        private static float VanillaMeltM(float depthM, float airC)
        {
            if (!s_MeltQ)
                return RunSnowRules.PackMeltM(MeltParams(2f), depthM, airC, Dt);
            double q = RunSnowRules.SnowHeightScaleM / 65535.0;
            double dtu = RunSnowRules.SnowUpdateIntervalFrames / (double)RunSnowRules.FramesPerGameHour;
            int updates = Math.Max(1, (int)Math.Round(Dt / dtu));
            double x = Math.Round(depthM / q) * q;
            for (int k = 0; k < updates && x > 0.0 && airC > 0f; k++)
            {
                double law = Math.Max(RunSnowRules.MeltFloorPerHourM, s_Melt * airC / x);
                x -= Math.Min(x, Math.Ceiling(law * dtu / q - 1e-6) * q);
            }
            return (float)Math.Max(0.0, depthM - x);
        }

        private sealed class Natural : Surface
        {
            public override void Step(float date, float tBase, float precip, float[] nat) => Array.Copy(nat, D, Cells);
        }

        // The proposal on a run: the game melts (Vanilla), the pack step builds; no hold, no managed melt.
        private sealed class GunRunProposed : Surface
        {
            private readonly RunSnowRules.PackParams m_P;
            private RunSnowRules.PackGun m_Gun;
            public GunRunProposed(RunSnowRules.PackParams p) { m_P = p; }
            public override void Step(float date, float tBase, float precip, float[] nat)
            {
                Vanilla(D, tBase, precip);
                bool season = SnowGunPlumeRules.InGunSeason("", date);
                double pack = 0;
                foreach (var x in D) pack += Math.Min(x, m_P.StopDepthM);
                m_Gun = RunSnowRules.NextPackGun(m_P, m_Gun, (float)(pack / Cells));
                GunsNow = false;
                for (int i = 0; i < Cells; i++)
                {
                    var r = RunSnowRules.PackStep(m_P, D[i], Local(tBase, CellY(i)), season, m_Gun, Dt, RunSnowRules.MeanRampRate, false);
                    D[i] = r.DepthM;
                    BuiltM += r.BuiltM / Cells;
                    GunsNow |= r.GunsMaking;
                }
                if (GunsNow) GunHours += Dt;
            }
        }

        // The live cat track (T222, wired): RunSnowRules.StepTrackPack on its 8 cells, the natural snow under it measured (the natural
        // column) as the gun decision's floor. (The 1.0.1 baselines, gun_today and track_today, were removed with the hold they modeled;
        // the round 2 snowpack report keeps their numbers.)
        private sealed class TrackLive : Surface
        {
            private readonly RunSnowRules.PackParams m_P;
            private RunSnowRules.TrackMadeSnow m_Made;
            private readonly float[] m_Nat = new float[Cells];
            public TrackLive(RunSnowRules.PackParams p) { m_P = p; }
            public override void Step(float date, float tBase, float precip, float[] nat)
            {
                Array.Copy(nat, m_Nat, Cells);
                bool season = SnowGunPlumeRules.InGunSeason("", date);
                float before = m_Made.Share * m_Made.DepthM;
                RunSnowRules.StepTrackPack(ref m_Made, m_P, BottomM, TopM, tBase, 0f, LapsePerM, season, season, Dt, Mean(m_Nat));
                GunsNow = RunSnowRules.TrackGunsBuilding(m_Made);
                if (GunsNow) GunHours += Dt;
                BuiltM += Math.Max(0f, m_Made.Share * m_Made.DepthM - before);
                for (int i = 0; i < Cells; i++) D[i] = m_Nat[i];
            }
            public override bool HasBands => false;
            public override float MeanDepth() => RunSnowRules.TrackMeanDepthM(Mean(m_Nat), m_Made);
            public override float CountedShare(RunSnowState prev) =>
                RunSnowRules.TrackShare(NatShare(m_Nat, prev), RunSnowRules.TrackMadeCountedShare(prev, m_Made));
        }

        // The proposal on a track: its cells are texels the mod owns. Same step, the mod melts, the measured natural snow is a floor.
        private sealed class TrackProposed : Surface
        {
            private readonly RunSnowRules.PackParams m_P;
            private RunSnowRules.PackGun m_Gun;
            private readonly float[] m_NatPrev = new float[Cells];
            public TrackProposed(RunSnowRules.PackParams p) { m_P = p; }
            public override bool HasBands => false;
            public override void Step(float date, float tBase, float precip, float[] nat)
            {
                bool season = SnowGunPlumeRules.InGunSeason("", date);
                // The surface part: the mod plays the game for its cells. It melts them at the shared rate, lays on the snowfall the
                // map shows under the track and floors them at the measured natural snow. Then the same measure, decision and build as
                // a run.
                for (int i = 0; i < Cells; i++)
                {
                    float melted = Math.Max(0f, D[i] - RunSnowRules.PackMeltM(m_P, D[i], Local(tBase, CellY(i)), Dt));
                    float fall = Math.Max(0f, nat[i] - m_NatPrev[i]);
                    m_NatPrev[i] = nat[i];
                    D[i] = Math.Max(melted + fall, nat[i]);
                }
                double pack = 0;
                foreach (var x in D) pack += Math.Min(x, m_P.StopDepthM);
                m_Gun = RunSnowRules.NextPackGun(m_P, m_Gun, (float)(pack / Cells));
                GunsNow = false;
                for (int i = 0; i < Cells; i++)
                {
                    var r = RunSnowRules.PackStep(m_P, D[i], Local(tBase, CellY(i)), season, m_Gun, Dt, RunSnowRules.MeanRampRate, false);
                    D[i] = r.DepthM;
                    BuiltM += r.BuiltM / Cells;
                    GunsNow |= r.GunsMaking;
                }
                if (GunsNow) GunHours += Dt;
            }
        }

        private static float s_MapMean;
        private static float MapMean(float tBase) => s_MapMean;

        private static float[] Fill(float v)
        {
            var a = new float[Cells];
            for (int i = 0; i < Cells; i++) a[i] = v;
            return a;
        }

        private static float Mean(float[] a)
        {
            double s = 0;
            foreach (var x in a) s += x;
            return (float)(s / a.Length);
        }

        private static float NatShare(float[] nat, RunSnowState prev)
        {
            int open = 0, keep = 0;
            foreach (var x in nat)
            {
                if (x >= RunSnowRules.SkiableDepthM) open++;
                if (x >= RunSnowRules.KeepDepthM) keep++;
            }
            return RunSnowRules.MeasuredShare(prev, open / (float)Cells, keep / (float)Cells);
        }

        // ------------------------------------------------------------------ climate
        private struct Hour { public float T, P; }

        private static Hour[] LoadClimate(string path)
        {
            var rows = new List<Hour>();
            foreach (var line in File.ReadAllLines(path))
            {
                if (line.StartsWith("hourIndex")) continue;
                var f = line.Split(',');
                rows.Add(new Hour { T = float.Parse(f[2], CultureInfo.InvariantCulture), P = float.Parse(f[4], CultureInfo.InvariantCulture) });
            }
            if (rows.Count < HoursPerYear) throw new InvalidDataException(path + ": " + rows.Count + " hours");
            return rows.ToArray();
        }

        private static List<Surface> MakeSurfaces()
        {
            var p096 = MeltParams(RunSnowRules.LegacyTrackStopDepthM);
            var p150 = MeltParams(1.5f);
            var p200 = MeltParams(2.0f);
            var p300 = MeltParams(3.0f);
            return new List<Surface>
            {
                new Natural { Name = "natural" },
                new GunRunProposed(p096) { Name = "gun_nohold_096" },
                new GunRunProposed(p150) { Name = "gun_prop_150" },
                new GunRunProposed(p200) { Name = "gun_prop_200" },
                new GunRunProposed(p300) { Name = "gun_prop_300" },
                new TrackProposed(p200) { Name = "track_prop_200" },
                new TrackLive(p200) { Name = "track_live_200" },
            };
        }

        private static string Stamp(float date)
        {
            string[] m = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
            float d = (date - (float)Math.Floor(date)) * 12f;
            int mi = (int)d;
            return m[mi] + " h" + ((int)((d - mi) * 24f)).ToString("00");
        }

        private static void RunClimate(string name, Hour[] hours, string outDir, StringBuilder summary)
        {
            var surfaces = MakeSurfaces();
            var nat = new float[Cells];
            int start = (int)Math.Round(RunSnowRules.SeasonRolloverDate * HoursPerYear);
            int steps = (int)(1f / Dt);
            var csv = new StringBuilder("hour,date,stamp,tBase,tTop,precip");
            foreach (var s in surfaces) csv.Append(',').Append(s.Name).Append("_m,").Append(s.Name).Append("_open,").Append(s.Name).Append("_guns");
            csv.AppendLine();
            var first = new Dictionary<string, string>();
            var last = new Dictionary<string, string>();
            for (int year = 0; year < 2; year++)
            {
                bool rec = year == 1;
                if (rec)
                    foreach (var s in surfaces) { s.GunHours = 0; s.BuiltM = 0; s.OpenHours = 0; s.OpenHoursC = 0; s.Peak = 0; s.PeakTop = 0; }
                for (int h = 0; h < HoursPerYear; h++)
                {
                    int hi = (start + h) % HoursPerYear;
                    Hour a = hours[hi], b = hours[(hi + 1) % HoursPerYear];
                    var gunsAny = new bool[surfaces.Count];
                    for (int k = 0; k < steps; k++)
                    {
                        float f = k * Dt;
                        float t = a.T + (b.T - a.T) * f;
                        float date = (hi + f) / HoursPerYear;
                        Vanilla(nat, t, a.P);
                        s_MapMean = Math.Max(0f, Mean(nat) * 0.25f);   // the map: mostly lower ground than the resort (assumption)
                        for (int j = 0; j < surfaces.Count; j++)
                        {
                            var s = surfaces[j];
                            s.Step(date, t, a.P, nat);
                            s.Label();
                            s.LabelC();
                            if (s.StateC == RunSnowState.Open) s.OpenHoursC += Dt;
                            gunsAny[j] |= s.GunsNow;
                            if (s.State == RunSnowState.Open) s.OpenHours += Dt;
                            s.Peak = Math.Max(s.Peak, s.MeanDepth());
                            s.PeakTop = Math.Max(s.PeakTop, s.D[Cells - 1]);
                            if (rec && s.State == RunSnowState.Open)
                            {
                                string st = Stamp(date);
                                if (!first.ContainsKey(s.Name)) first[s.Name] = st;
                                last[s.Name] = st;
                            }
                        }
                    }
                    if (!rec) continue;
                    float dateH = hi / (float)HoursPerYear;
                    csv.Append(h).Append(',').Append(dateH.ToString("0.0000")).Append(',').Append(Stamp(dateH)).Append(',')
                       .Append(a.T.ToString("0.00")).Append(',').Append(Local(a.T, TopM).ToString("0.00")).Append(',').Append(a.P.ToString("0.000"));
                    for (int j = 0; j < surfaces.Count; j++)
                    {
                        var s = surfaces[j];
                        csv.Append(',').Append(s.MeanDepth().ToString("0.000")).Append(',').Append(s.State == RunSnowState.Open ? 1 : 0)
                           .Append(',').Append(gunsAny[j] ? 1 : 0);
                    }
                    csv.AppendLine();
                }
            }
            string tag = name + "_melt" + s_Melt.ToString("0.000");
            File.WriteAllText(Path.Combine(outDir, tag + ".csv"), csv.ToString());
            summary.AppendLine("| " + name + " | " + s_Melt.ToString("0.000") + " | surface | open h | open h, top to bottom (T268) | first open | last open | gun h | made m | peak mean m | peak top m |");
            foreach (var s in surfaces)
            {
                summary.AppendLine("| | | " + s.Name + " | " + s.OpenHours.ToString("0.0") + " | " + (s.HasBands ? s.OpenHoursC.ToString("0.0") : "area rule") + " | " + (first.TryGetValue(s.Name, out var fo) ? fo : "-") + " | "
                    + (last.TryGetValue(s.Name, out var lo) ? lo : "-") + " | " + s.GunHours.ToString("0.0") + " | " + s.BuiltM.ToString("0.00")
                    + " | " + s.Peak.ToString("0.00") + " | " + s.PeakTop.ToString("0.00") + " |");
            }
        }

        private static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            if (args.Length < 2)
            {
                Console.WriteLine("usage: SnowPackSim <climate csv dir> <out dir> [melt depth-term list (m^2/h/C), default 0.0425] [add m per precip-hour, default 0.35]");
                return 2;
            }
            string dir = args[0], outDir = args[1];
            Directory.CreateDirectory(outDir);
            var melts = args.Length > 2 ? args[2].Split(',') : new[] { "0.0425" };
            s_AddPerPrecipHourM = args.Length > 3 ? float.Parse(args[3], CultureInfo.InvariantCulture) : 0.35f;
            var summary = new StringBuilder();
            summary.AppendLine("Cells " + Cells + " from " + BottomM + " to " + TopM + " m above the base height, add " + s_AddPerPrecipHourM
                + " m per precipitation-hour at or below 0 C, step " + Dt + " h, season from the 0.6 rollover, second year recorded, snow map melt "
                + (s_MeltQ ? "in whole R16 quanta per 4-frame update (SIM_MELT_Q)" : "by the continuous law") + ".");
            summary.AppendLine();
            foreach (var climate in new[] { "ContinentalMountainVillageClimate", "PolarTwinMountainClimate", "TemperateRiverDeltaClimate" })
            {
                var hours = LoadClimate(Path.Combine(dir, climate + ".csv"));
                foreach (var m in melts)
                {
                    s_Melt = float.Parse(m, CultureInfo.InvariantCulture);
                    RunClimate(climate, hours, outDir, summary);
                }
            }
            File.WriteAllText(Path.Combine(outDir, "summary.md"), summary.ToString());
            Console.Write(summary.ToString());
            return 0;
        }
    }
}
