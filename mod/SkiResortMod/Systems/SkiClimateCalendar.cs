// The running climate's winter, read from its prefab, for the rules that follow the map's own calendar. Those rules are the gun season, the
// spring fallback, the snow ledger's rollover and the ski season of passes and history. A map may have its winter in June (DECISIONS 472,
// 475).
// The rules themselves stay in pure files (SnowLineRules, SkiMoneyRules, RunSnowRules, SnowGunPlumeRules).

using Game.Prefabs;
using Game.Prefabs.Climate;
using Game.Simulation;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    internal static class SkiClimateCalendar
    {
        private const string WinterSeasonName = "SeasonWinter";

        // A climate prefab's seasons do not change while the game runs. The last read stands until the climate entity changes. This code
        // runs on the main thread.
        private static Entity s_Climate = Entity.Null;
        private static float s_Start = SnowLineRules.CalendarWinterStart, s_End = SnowLineRules.CalendarWinterEnd;

        /// <summary>The climate's winter in normalized year time (SnowLineRules.WinterWindow). With no climate or no winter season, it
        /// reads the calendar winter, December to February.</summary>
        public static void WinterWindow(ClimateSystem climates, PrefabSystem prefabs, out float start, out float end)
        {
            Entity climate = climates.currentClimate;
            if (climate != s_Climate)
            {
                ClimatePrefab prefab = climate != Entity.Null ? prefabs.GetPrefab<ClimatePrefab>(climate) : null;
                ClimateSystem.SeasonInfo[] seasons = prefab != null ? prefab.m_Seasons : null;
                float[] starts = null;
                int winter = -1;
                if (seasons != null && seasons.Length > 0)
                {
                    starts = new float[seasons.Length];
                    for (int i = 0; i < seasons.Length; i++)
                    {
                        starts[i] = seasons[i] != null ? seasons[i].m_StartTime : 0f;
                        if (winter < 0 && seasons[i] != null && seasons[i].name == WinterSeasonName)
                            winter = i;
                    }
                }
                SnowLineRules.WinterWindow(starts, winter, out s_Start, out s_End);
                s_Climate = climate;
            }
            start = s_Start;
            end = s_End;
        }

        /// <summary>The first calendar month (1..12) of the ski season on this map (SkiMoneyRules.SeasonFirstMonthOf).</summary>
        public static int SeasonFirstMonth(ClimateSystem climates, PrefabSystem prefabs)
        {
            WinterWindow(climates, prefabs, out float start, out float _);
            return SkiMoneyRules.SeasonFirstMonthOf(start);
        }
    }
}
