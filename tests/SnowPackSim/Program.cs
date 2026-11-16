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
        private const float BottomM = 300f, TopM = 1000f, LapsePerM = 0.01f;
        private const float Dt = 0.25f;
        private const int HoursPerYear = 288;
        private const float MarginC = RunSnowRules.DefaultSnowmakingMarginC;
        private static float s_AddPerPrecipHourM;
        private static float s_Melt;

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
        }

        private static void Vanilla(float[] d, float tBase, float precip)
        {
            for (int i = 0; i < Cells; i++)
            {
                float t = Local(tBase, CellY(i));
                if (t <= 0f) d[i] += s_AddPerPrecipHourM * precip * Dt;
                else d[i] = Math.Max(0f, d[i] - s_Melt * t * Dt);
            }
        }

        private sealed class Natural : Surface
        {
            public override void Step(float date, float tBase, float precip, float[] nat) => Array.Copy(nat, D, Cells);
        }

        // Today's gun run: injection raise (run-level gun hysteresis at 0.96 m), the protection hold (run machine + per-texel budget,
        // aspect 1), managed melt (0.004 m per window on warm unprotected texels while the map mean is under 0.02 m).
        private sealed class GunRunToday : Surface
        {
            private SnowGunPlumeRules.GunState m_Gun;
            private RunSnowRules.ProtectRun m_Spell;
            private readonly float[] m_Base = Fill(float.NaN);
            public override void Step(float date, float tBase, float precip, float[] nat)
            {
                Vanilla(D, tBase, precip);
                bool season = SnowGunPlumeRules.InGunSeason("", date);
                bool spring = RunSnowRules.IsSpring("", date);
                float stop = RunSnowRules.GunStopDepthM, thr = MarginC;
                double pack = 0;
                foreach (var x in D) pack += Math.Min(x, stop);
                m_Gun = SnowGunPlumeRules.NextGunState(m_Gun, SnowGunPlumeRules.PackDepthM(Cells * stop, pack));
                bool warm = false;
                for (int i = 0; i < Cells; i++) warm |= Local(tBase, CellY(i)) > thr;
                float runC = Local(tBase, 0.5f * (BottomM + TopM));
                RunSnowRules.StepProtection(ref m_Spell, Dt, warm, State == RunSnowState.Open, runC, spring, out _);
                bool mapPack = RunSnowRules.PackGuard(MapMean(tBase));
                GunsNow = false;
                for (int i = 0; i < Cells; i++)
                {
                    float t = Local(tBase, CellY(i));
                    if (t <= thr)
                    {
                        m_Base[i] = float.NaN;
                        if (season && m_Gun == SnowGunPlumeRules.GunState.Running && D[i] < stop)
                        {
                            float add = Math.Min(stop - D[i], RunSnowRules.PackBuildPerHourM(t, thr, RunSnowRules.MeanRampRate) * Dt);
                            D[i] += add;
                            BuiltM += add / Cells;
                            GunsNow = true;
                        }
                        continue;
                    }
                    if (m_Spell.State == RunSnowRules.ProtectState.Active && RunSnowRules.TexelProtected(m_Spell.HoursUsed, t, spring, 1f))
                    {
                        if (float.IsNaN(m_Base[i])) m_Base[i] = Math.Min(D[i] + s_Melt * t * Dt, stop);   // the depth when the hold began
                        float up = Math.Min(m_Base[i] - D[i], RunSnowRules.ProtectRaisePerTopupM * RunSnowRules.WindowsPerGameHour * Dt);
                        if (up > 0f) D[i] += up;
                        continue;
                    }
                    m_Base[i] = float.NaN;
                    if (!mapPack) D[i] = Math.Max(0f, D[i] - 0.004f * RunSnowRules.WindowsPerGameHour * Dt);
                }
                if (GunsNow) GunHours += Dt;
            }
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

        // Today's cat track: StepTrackMadeSnow + StepTrackGunWork, the natural snow under it measured (the natural column).
        private sealed class TrackToday : Surface
        {
            private RunSnowRules.TrackMadeSnow m_Made;
            private float[] m_Nat = new float[Cells];
            public override void Step(float date, float tBase, float precip, float[] nat)
            {
                Array.Copy(nat, m_Nat, Cells);
                bool season = SnowGunPlumeRules.InGunSeason("", date);
                bool spring = RunSnowRules.IsSpring("", date);
                float gun = RunSnowRules.TrackGunShare(season, true, BottomM, TopM, tBase, 0f, MarginC, 0f, LapsePerM);
                float cold = RunSnowRules.TrackColdShare(BottomM, TopM, tBase, 0f, 0f, LapsePerM);
                float trackC = Local(tBase, 0.5f * (BottomM + TopM));
                float before = m_Made.Share, depthBefore = m_Made.DepthM;
                var endT = RunSnowRules.StepTrackMadeSnow(ref m_Made, Dt, gun, cold, trackC, spring, out bool startedT);
                if (Environment.GetEnvironmentVariable("SNOWPACK_DEBUG") == "1" && (startedT || endT != RunSnowRules.ProtectEnd.None))
                    Console.WriteLine("[track] " + Stamp(date) + " " + (startedT ? "start" : endT.ToString()) + " share " + m_Made.Share.ToString("0.00") + " depth " + m_Made.DepthM.ToString("0.00") + " trackC " + trackC.ToString("0.0") + " cold " + cold.ToString("0.00"));
                RunSnowRules.StepTrackGunWork(ref m_Made, before, gun, NatShare(m_Nat, RunSnowState.Closed), Dt);
                GunsNow = RunSnowRules.TrackGunsWorking(m_Made);
                if (GunsNow) GunHours += Dt;
                BuiltM += Math.Max(0f, m_Made.DepthM * m_Made.Share - depthBefore * before);
                for (int i = 0; i < Cells; i++) D[i] = m_Nat[i];
            }
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
            public override void Step(float date, float tBase, float precip, float[] nat)
            {
                bool season = SnowGunPlumeRules.InGunSeason("", date);
                // The surface part: the mod plays the game for its cells. It melts them at the shared rate, lays on the snowfall the
                // map shows under the track and floors them at the measured natural snow. Then the same measure, decision and build as
                // a run.
                for (int i = 0; i < Cells; i++)
                {
                    float melted = Math.Max(0f, D[i] - RunSnowRules.PackMeltPerHourM(m_P, Local(tBase, CellY(i))) * Dt);
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
            var p096 = RunSnowRules.PackParams.With(RunSnowRules.GunStopDepthM, s_Melt);
            var p150 = RunSnowRules.PackParams.With(RunSnowRules.ProposedStopDepthM, s_Melt);
            var p200 = RunSnowRules.PackParams.With(2.0f, s_Melt);
            var p300 = RunSnowRules.PackParams.With(3.0f, s_Melt);
            return new List<Surface>
            {
                new Natural { Name = "natural" },
                new GunRunToday { Name = "gun_today" },
                new GunRunProposed(p096) { Name = "gun_nohold_096" },
                new GunRunProposed(p150) { Name = "gun_prop_150" },
                new GunRunProposed(p200) { Name = "gun_prop_200" },
                new GunRunProposed(p300) { Name = "gun_prop_300" },
                new TrackToday { Name = "track_today" },
                new TrackProposed(p200) { Name = "track_prop_200" },
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
                    foreach (var s in surfaces) { s.GunHours = 0; s.BuiltM = 0; s.OpenHours = 0; s.Peak = 0; s.PeakTop = 0; }
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
            summary.AppendLine("| " + name + " | " + s_Melt.ToString("0.000") + " | surface | open h | first open | last open | gun h | made m | peak mean m | peak top m |");
            foreach (var s in surfaces)
            {
                summary.AppendLine("| | | " + s.Name + " | " + s.OpenHours.ToString("0.0") + " | " + (first.TryGetValue(s.Name, out var fo) ? fo : "-") + " | "
                    + (last.TryGetValue(s.Name, out var lo) ? lo : "-") + " | " + s.GunHours.ToString("0.0") + " | " + s.BuiltM.ToString("0.00")
                    + " | " + s.Peak.ToString("0.00") + " | " + s.PeakTop.ToString("0.00") + " |");
            }
        }

        private static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            if (args.Length < 2)
            {
                Console.WriteLine("usage: SnowPackSim <climate csv dir> <out dir> [melt m/h/C list, default 0.109,0.26] [add m per precip-hour, default 0.35]");
                return 2;
            }
            string dir = args[0], outDir = args[1];
            Directory.CreateDirectory(outDir);
            var melts = args.Length > 2 ? args[2].Split(',') : new[] { "0.109227", "0.26" };
            s_AddPerPrecipHourM = args.Length > 3 ? float.Parse(args[3], CultureInfo.InvariantCulture) : 0.35f;
            var summary = new StringBuilder();
            summary.AppendLine("Cells " + Cells + " from " + BottomM + " to " + TopM + " m above the base height, add " + s_AddPerPrecipHourM
                + " m per precipitation-hour at or below 0 C, step " + Dt + " h, season from the 0.6 rollover, second year recorded.");
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
