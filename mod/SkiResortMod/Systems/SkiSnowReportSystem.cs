// The ski area snow report (T200, docs/research/post-release-round2/T200-snow-report.md). Pull-based like SnowLineSystem: Enabled = false,
// no per-tick work. The stats rebuild (hub) and the Welcome Sign card (SkiResortHubUISystem) call TryGetReport for a sign.
//   Elevations: the lowest and highest outline node of the sign's bound runs (cat tracks excluded, the hub's vertical rule), cached per
//   catalog Revision.
//   Temperature now: SnowLineSystem.GetLocalTemperature at each elevation.
//   Depth: SkiSnowInjectionSystem.TrySampleDepthM at each node (the newest snapshot, no readback of its own). -1 without one.
//   Snowfall (last 24 hours, season): SnowReportRules over one climate sample per hour of the year. Refilled at the first read of a load,
//   at a game year roll, a climate change or a new day count (the SkiDaySystem powder-year rule: 24 x days SampleClimate calls).
// Nothing is saved: every number is rebuilt from the date, the climate and the live snow map.

using System;
using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Colossal.UI.Binding;
using Game;
using Game.Prefabs;
using Game.Simulation;
using Unity.Entities;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>bindings.ts SnowReport: one ski area's snow report. Elevations in m, temperatures in °C, depths and snowfall in whole cm.
    /// -1 = unknown for every cm field. Known false: the area has no run with an outline (every other field is then unknown).</summary>
    public struct SnowReport : IEquatable<SnowReport>
    {
        public bool Known;
        public float BaseM, SummitM;
        public bool TempKnown;
        public float BaseTempC, SummitTempC;
        public int BaseDepthCm, SummitDepthCm;
        public int BaseNew24Cm, SummitNew24Cm;
        public int BaseSeasonCm, SummitSeasonCm;
        public int SeasonFirstMonth;   // 1..12, 0 unknown

        public static SnowReport Unknown => new SnowReport
        {
            BaseDepthCm = -1, SummitDepthCm = -1, BaseNew24Cm = -1, SummitNew24Cm = -1, BaseSeasonCm = -1, SummitSeasonCm = -1,
        };

        /// <summary>Every field on every path (Gameface: one TypeBegin name, one property set).</summary>
        public void Write(IJsonWriter writer)
        {
            writer.TypeBegin("SkiResortMod.SnowReport");
            writer.PropertyName("known");
            writer.Write(Known);
            writer.PropertyName("baseM");
            writer.Write(Fin(BaseM));
            writer.PropertyName("summitM");
            writer.Write(Fin(SummitM));
            writer.PropertyName("tempKnown");
            writer.Write(TempKnown);
            writer.PropertyName("baseTempC");
            writer.Write(Fin(BaseTempC));
            writer.PropertyName("summitTempC");
            writer.Write(Fin(SummitTempC));
            writer.PropertyName("baseDepthCm");
            writer.Write(BaseDepthCm);
            writer.PropertyName("summitDepthCm");
            writer.Write(SummitDepthCm);
            writer.PropertyName("baseNew24Cm");
            writer.Write(BaseNew24Cm);
            writer.PropertyName("summitNew24Cm");
            writer.Write(SummitNew24Cm);
            writer.PropertyName("baseSeasonCm");
            writer.Write(BaseSeasonCm);
            writer.PropertyName("summitSeasonCm");
            writer.Write(SummitSeasonCm);
            writer.PropertyName("seasonFirstMonth");
            writer.Write(SeasonFirstMonth);
            writer.TypeEnd();
        }

        private static float Fin(float v) => float.IsNaN(v) || float.IsInfinity(v) ? 0f : v;

        public bool Equals(SnowReport o) =>
            Known == o.Known && BaseM.Equals(o.BaseM) && SummitM.Equals(o.SummitM) && TempKnown == o.TempKnown &&
            BaseTempC.Equals(o.BaseTempC) && SummitTempC.Equals(o.SummitTempC) && BaseDepthCm == o.BaseDepthCm &&
            SummitDepthCm == o.SummitDepthCm && BaseNew24Cm == o.BaseNew24Cm && SummitNew24Cm == o.SummitNew24Cm &&
            BaseSeasonCm == o.BaseSeasonCm && SummitSeasonCm == o.SummitSeasonCm && SeasonFirstMonth == o.SeasonFirstMonth;

        public override bool Equals(object obj) => obj is SnowReport o && Equals(o);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = Known ? 1 : 0;
                h = h * 31 + BaseDepthCm;
                h = h * 31 + SummitDepthCm;
                h = h * 31 + SummitSeasonCm;
                h = h * 31 + SummitNew24Cm;
                return h;
            }
        }
    }

    public sealed partial class SkiSnowReportSystem : GameSystemBase
    {
        private struct Extremes
        {
            public bool Known;
            public float BaseM, SummitM;
            public float2 BaseXZ, SummitXZ;
        }

        private SkiRunCatalogSystem m_Catalog;
        private SkiSnowInjectionSystem m_Injection;
        private SnowLineSystem m_SnowLine;
        private ClimateSystem m_Climate;
        private TimeSystem m_Time;
        private PrefabSystem m_Prefabs;

        private readonly Dictionary<Entity, Extremes> m_Extremes = new Dictionary<Entity, Extremes>();
        private int m_ExtremesRevision = int.MinValue;

        private float[] m_Precip, m_Ambient;
        private bool m_YearValid;
        private Entity m_YearClimate;
        private int m_YearDays, m_YearYear;
        private bool m_YearLogged;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Catalog = World.GetOrCreateSystemManaged<SkiRunCatalogSystem>();
            m_Injection = World.GetOrCreateSystemManaged<SkiSnowInjectionSystem>();
            m_SnowLine = World.GetOrCreateSystemManaged<SnowLineSystem>();
            m_Climate = World.GetOrCreateSystemManaged<ClimateSystem>();
            m_Time = World.GetOrCreateSystemManaged<TimeSystem>();
            m_Prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            Enabled = false;
            Mod.Log.Info($"{nameof(SkiSnowReportSystem)} created (snow report, pull-based, nothing saved)");
        }

        protected override void OnUpdate()
        {
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Extremes.Clear();
            m_ExtremesRevision = int.MinValue;
            m_YearValid = false;
            m_YearLogged = false;
        }

        /// <summary>The report of the ski area of <paramref name="sign"/>. Main thread. Known false without a run outline.</summary>
        public SnowReport GetReport(Entity sign)
        {
            SnowReport r = SnowReport.Unknown;
            if (sign == Entity.Null)
                return r;
            RefreshExtremes();
            if (!m_Extremes.TryGetValue(sign, out Extremes x) || !x.Known)
                return r;
            r.Known = true;
            r.BaseM = x.BaseM;
            r.SummitM = x.SummitM;
            bool climate = m_Climate.currentClimate != Entity.Null;
            if (climate)
            {
                r.TempKnown = true;
                r.BaseTempC = m_SnowLine.GetLocalTemperature(x.BaseM);
                r.SummitTempC = m_SnowLine.GetLocalTemperature(x.SummitM);
            }
            if (m_Injection.TrySampleDepthM(x.BaseXZ, out float baseD))
                r.BaseDepthCm = SnowReportRules.DepthCm(baseD);
            if (m_Injection.TrySampleDepthM(x.SummitXZ, out float summitD))
                r.SummitDepthCm = SnowReportRules.DepthCm(summitD);
            if (climate && RefreshYear())
            {
                int days = Math.Max(1, m_Time.daysPerYear);
                RunSnowRules.HourAlignedDate((float)m_Climate.currentDate, days, out int hourKey);
                int first = SkiClimateCalendar.SeasonFirstMonth(m_Climate, m_Prefabs);
                int seasonHours = SnowReportRules.SeasonHours(hourKey, SnowReportRules.MonthStartHour(first - 1, days), m_Precip.Length);
                float baseH = m_SnowLine.GetBaseHeight(), lapse = m_SnowLine.GetLapsePerM(), freezing = m_SnowLine.GetFreezingTemperature();
                r.SeasonFirstMonth = first;
                r.BaseNew24Cm = Whole(SnowReportRules.HoursInWindow, hourKey, x.BaseM, baseH, lapse, freezing);
                r.SummitNew24Cm = Whole(SnowReportRules.HoursInWindow, hourKey, x.SummitM, baseH, lapse, freezing);
                r.BaseSeasonCm = Whole(seasonHours, hourKey, x.BaseM, baseH, lapse, freezing);
                r.SummitSeasonCm = Whole(seasonHours, hourKey, x.SummitM, baseH, lapse, freezing);
            }
            return r;
        }

        private int Whole(int hours, int hourKey, float elevation, float baseH, float lapse, float freezing) =>
            SnowReportRules.WholeCm(SnowReportRules.SnowfallCm(m_Precip, m_Ambient, hourKey, hours, elevation, baseH, lapse, freezing));

        /// <summary>Per sign: the lowest and highest outline node of its bound runs (cat tracks excluded). Once per catalog Revision.</summary>
        private void RefreshExtremes()
        {
            if (m_Catalog.Revision == m_ExtremesRevision)
                return;
            m_ExtremesRevision = m_Catalog.Revision;
            m_Extremes.Clear();
            int count = m_Catalog.RowCount;
            for (int i = 0; i < count; i++)
            {
                SkiRunCatalogSystem.RunRow row = m_Catalog.GetRow(i);
                if (row.Gate == Entity.Null || row.Kind == (int)RunKind.CatTrack || row.OutlineXZ == null || row.OutlineY == null ||
                    row.OutlineY.Length != row.OutlineXZ.Length)
                    continue;
                m_Extremes.TryGetValue(row.Gate, out Extremes x);
                for (int k = 0; k < row.OutlineY.Length; k++)
                {
                    float y = row.OutlineY[k];
                    if (float.IsNaN(y) || float.IsInfinity(y))
                        continue;
                    if (!x.Known || y < x.BaseM)
                    {
                        x.BaseM = y;
                        x.BaseXZ = row.OutlineXZ[k];
                    }
                    if (!x.Known || y > x.SummitM)
                    {
                        x.SummitM = y;
                        x.SummitXZ = row.OutlineXZ[k];
                    }
                    x.Known = true;
                }
                m_Extremes[row.Gate] = x;
            }
        }

        /// <summary>One climate sample per hour of the year (index = hour key). False without a climate.</summary>
        private bool RefreshYear()
        {
            Entity climate = m_Climate.currentClimate;
            if (climate == Entity.Null)
                return false;
            int days = Math.Max(1, m_Time.daysPerYear);
            int year = m_Time.year;
            int hours = 24 * days;
            if (m_YearValid && m_YearClimate == climate && m_YearDays == days && m_YearYear == year && m_Precip != null && m_Precip.Length == hours)
                return true;
            if (m_Precip == null || m_Precip.Length != hours)
            {
                m_Precip = new float[hours];
                m_Ambient = new float[hours];
            }
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            for (int h = 0; h < hours; h++)
            {
                ClimateSystem.ClimateSample s = m_Climate.SampleClimate(SkiDayRules.HourDate(h, days));
                m_Precip[h] = s.precipitation;
                m_Ambient[h] = s.temperature;
            }
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            string why = !m_YearValid ? (m_YearLogged ? "a climate change" : "first read this load")
                : m_YearYear != year ? "the year roll" : m_YearClimate != climate ? "a climate change" : "a new day count";
            m_YearValid = true;
            m_YearClimate = climate;
            m_YearDays = days;
            m_YearYear = year;
            m_YearLogged = true;
            Mod.Log.Info($"SnowReport [year] {hours} hourly climate samples in {ms:F1} ms ({why}): snowfall = precipitation x " +
                         $"{SnowReportRules.CmPerPrecipHour} cm per hour that snows at the elevation (T200, nothing saved)");
            return true;
        }
    }
}
