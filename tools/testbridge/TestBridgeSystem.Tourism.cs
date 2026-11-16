// T225 / T226 tourism lab (round 2): read-only numbers on the city's tourists, the resort's share and the attraction curve, plus a
// sampler for the daily shape and the arrivals. One write: `bonus` sets the prototype's hidden developer setting (only a build with
// ResortAttractionDevPct has it; the published mod has none).
//
//   {op:"tourism"}                               one snapshot (see Snapshot)
//   {op:"tourism", action:"start", every:2048}   sample every N simulation frames (default 2048 = 128 a game day); clears the samples
//   {op:"tourism", action:"read", from:0}        samples from index `from`, plus the arrivals so far
//   {op:"tourism", action:"stop"}
//   {op:"tourism", action:"bonus", pct:10}       the "Resort tourism bonus" option (SkiResortSettings.ResortTourismBonusPct, 1.0.2+, int 0-20),
//                                                else the prototype's ResortAttractionDevPct (r2/tourism-lab builds)
//   {op:"tourism", action:"income"}              the TouristIncome and TouristCount statistics: last value, collection type, the last 40
//                                                samples (32 a game day); T226 "Value to the city" under Tourism Overhaul
//
// Vanilla formulas (decompile 1.6.4f1 TourismSystem.cs:95-106, 171-180): s = sum a^2 / 10^4 over every AttractivenessProvider,
// A0 = 200 / (1 + e^(-0.3 s)) - 100, A = A0 + delta.x, then A += A * delta.y (CityUtils.ApplyModifier), rounded; target = 15 A up to
// 100, then 1500 + 100 log10(1 + (A - 100)).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.Buildings;
using Game.Citizens;
using Game.City;
using Game.Common;
using Game.Companies;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Entities;

namespace SkiTestBridge
{
    public partial class TestBridgeSystem
    {
        private bool m_TourOn;
        private int m_TourEvery = 2048;
        private uint m_TourLastFrame;
        private readonly List<JObject> m_TourSamples = new List<JObject>();
        private HashSet<Entity> m_TourSeen = new HashSet<Entity>();
        private int m_TourArrivals, m_TourArrivalPeople;
        private CountHouseholdDataSystem m_TourCount;

        private JObject TourismCommand(JObject args)
        {
            switch ((string)args["action"] ?? "snapshot")
            {
                case "snapshot":
                    return TourSnapshot(true);
                case "start":
                    m_TourEvery = Math.Max(256, (int?)args["every"] ?? 2048);
                    m_TourSamples.Clear();
                    m_TourSeen = TourHouseholds(out _, out _);
                    m_TourArrivals = m_TourArrivalPeople = 0;
                    m_TourLastFrame = m_Simulation.frameIndex;
                    m_TourOn = true;
                    return new JObject { ["started"] = true, ["every"] = m_TourEvery, ["households"] = m_TourSeen.Count };
                case "read":
                {
                    int from = Math.Max(0, (int?)args["from"] ?? 0);
                    return new JObject
                    {
                        ["on"] = m_TourOn,
                        ["count"] = m_TourSamples.Count,
                        ["arrivals"] = m_TourArrivals,
                        ["arrivalPeople"] = m_TourArrivalPeople,
                        ["samples"] = new JArray(m_TourSamples.Skip(from).ToArray()),
                    };
                }
                case "stop":
                    m_TourOn = false;
                    return new JObject { ["stopped"] = true, ["count"] = m_TourSamples.Count };
                case "bonus":
                    return TourBonus(args);
                case "income":
                    return TourIncome();
                default:
                    throw new ArgumentException("action: snapshot, start, read, stop, bonus, income");
            }
        }

        private void TickTourism()
        {
            if (!m_TourOn || m_City.City == Entity.Null)
                return;
            uint frame = m_Simulation.frameIndex;
            if (frame - m_TourLastFrame < (uint)m_TourEvery)
                return;
            m_TourLastFrame = frame;
            try
            {
                JObject row = TourSnapshot(false);
                HashSet<Entity> now = TourHouseholds(out _, out Dictionary<Entity, int> sizes);
                int fresh = 0, people = 0;
                foreach (Entity e in now)
                {
                    if (!m_TourSeen.Contains(e))
                    {
                        fresh++;
                        people += sizes[e];
                    }
                }
                m_TourSeen = now;
                m_TourArrivals += fresh;
                m_TourArrivalPeople += people;
                row["newHouseholds"] = fresh;
                row["newPeople"] = people;
                m_TourSamples.Add(row);
            }
            catch (Exception e)
            {
                Mod.Log.Warn("[tourism] sample failed: " + e);
                m_TourOn = false;
            }
        }

        /// <summary>Live tourist households (not Deleted / Temp) and their citizen counts.</summary>
        private HashSet<Entity> TourHouseholds(out int withHotel, out Dictionary<Entity, int> sizes)
        {
            var set = new HashSet<Entity>();
            sizes = new Dictionary<Entity, int>();
            withHotel = 0;
            EntityQuery q = EntityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<TouristHousehold>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });
            using (NativeArray<Entity> es = q.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity e in es)
                {
                    set.Add(e);
                    int n = EntityManager.HasBuffer<HouseholdCitizen>(e) ? EntityManager.GetBuffer<HouseholdCitizen>(e, true).Length : 0;
                    sizes[e] = n;
                    if (EntityManager.GetComponentData<TouristHousehold>(e).m_Hotel != Entity.Null)
                        withHotel++;
                }
            }
            q.Dispose();
            return set;
        }

        private JObject TourSnapshot(bool full)
        {
            if (m_City.City == Entity.Null)
                throw new InvalidOperationException("no city loaded");
            m_TourCount = m_TourCount ?? World.GetOrCreateSystemManaged<CountHouseholdDataSystem>();
            Tourism t = EntityManager.GetComponentData<Tourism>(m_City.City);
            float dx = 0f, dy = 0f;
            if (EntityManager.HasBuffer<CityModifier>(m_City.City))
            {
                DynamicBuffer<CityModifier> mods = EntityManager.GetBuffer<CityModifier>(m_City.City, true);
                if (mods.Length > (int)CityModifierType.Attractiveness)
                {
                    dx = mods[(int)CityModifierType.Attractiveness].m_Delta.x;
                    dy = mods[(int)CityModifierType.Attractiveness].m_Delta.y;
                }
            }

            // Sum of a^2 / 10^4: all providers, and ours (a Ski* prefab or a merged lodge).
            Type mergedType = FindTypeByName("SkiResortMod.Systems.SkiMergedLodge");
            double sAll = 0, sOurs = 0;
            int providers = 0, ours = 0;
            var top = new List<KeyValuePair<string, int>>();
            EntityQuery pq = EntityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<AttractivenessProvider>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });
            using (NativeArray<Entity> es = pq.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity e in es)
                {
                    int a = EntityManager.GetComponentData<AttractivenessProvider>(e).m_Attractiveness;
                    double sq = (double)a * a / 10000.0;
                    sAll += sq;
                    providers++;
                    string name = EntityManager.HasComponent<PrefabRef>(e) ? m_Prefabs.GetPrefabName(EntityManager.GetComponentData<PrefabRef>(e).m_Prefab) : "?";
                    bool merged = mergedType != null && EntityManager.HasComponent(e, ComponentType.ReadOnly(mergedType));
                    if (name.StartsWith("Ski", StringComparison.Ordinal) || merged)
                    {
                        sOurs += sq;
                        ours++;
                    }
                    if (full)
                        top.Add(new KeyValuePair<string, int>((merged ? "[merged] " : "") + name, a));
                }
            }
            pq.Dispose();

            double a0 = Logistic(sAll), a0Without = Logistic(sAll - sOurs);
            double aMod = a0 + dx; aMod += aMod * dy;
            double aModWithout = a0Without + dx; aModWithout += aModWithout * dy;
            HashSet<Entity> hh = TourHouseholds(out int withHotel, out Dictionary<Entity, int> sizes);
            int people = sizes.Values.Sum(), empty = sizes.Values.Count(n => n == 0);

            var row = new JObject
            {
                ["frame"] = m_Simulation.frameIndex,
                ["time"] = TimeState(),
                ["current"] = t.m_CurrentTourists,
                ["vanillaTouristCount"] = m_TourCount.TouristCitizenCount,
                ["households"] = hh.Count,
                ["householdsWithHotel"] = withHotel,
                ["householdsEmpty"] = empty,
                ["people"] = people,
                ["A"] = t.m_Attractiveness,
                ["A0"] = Math.Round(a0, 2),
                ["AmodCalc"] = Math.Round(aMod, 2),
                ["AwithoutUs"] = Math.Round(aModWithout, 2),
                ["mod"] = new JArray(Math.Round(dx, 4), Math.Round(dy, 4)),
                ["sAll"] = Math.Round(sAll, 3),
                ["sOurs"] = Math.Round(sOurs, 3),
                ["target"] = TourismSystem.GetTargetTourists(t.m_Attractiveness),
                ["targetWithoutUs"] = TourismSystem.GetTargetTourists((int)Math.Round(aModWithout)),
                ["lodging"] = new JArray(t.m_Lodging.x, t.m_Lodging.y),
                ["average"] = t.m_AverageTourists,
            };
            if (!full)
            {
                row["bonusApplied"] = TourBonusSystemState()?["applied"];
                return row;
            }

            row["providers"] = providers;
            row["ourProviders"] = ours;
            row["topProviders"] = new JArray(top.OrderByDescending(kv => kv.Value).Take(15).Select(kv => kv.Key + " " + kv.Value).ToArray());
            row["tourismOverhaul"] = AppDomain.CurrentDomain.GetAssemblies().Any(asm => asm.GetName().Name.IndexOf("TourismOverhaul", StringComparison.OrdinalIgnoreCase) >= 0);
            row["vanillaSpawnEnabled"] = World.GetExistingSystemManaged<TouristSpawnSystem>()?.Enabled;
            row["vanillaFindTargetEnabled"] = World.GetExistingSystemManaged<TouristFindTargetSystem>()?.Enabled;
            row["hotels"] = TourHotels(mergedType);
            try { row["ledger"] = LcLedger(); } catch (Exception e) { row["ledger"] = "unavailable: " + e.Message; }
            row["bonusSetting"] = TourBonusValue();
            row["bonusSystem"] = TourBonusSystemState();
            try { row["touristIncome"] = TourStatistic(StatisticType.TouristIncome, 0); } catch (Exception e) { row["touristIncome"] = "unavailable: " + e.Message; }
            return row;
        }

        private static double Logistic(double s) => 200.0 / (1.0 + Math.Exp(-0.3 * s)) - 100.0;

        /// <summary>Every lodging company: renters (one tourist household per room) and free rooms; merged lodges listed by name.</summary>
        private JObject TourHotels(Type mergedType)
        {
            int companies = 0, rented = 0, rooms = 0;
            var merged = new JArray();
            EntityQuery q = EntityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<LodgingProvider>(), ComponentType.ReadOnly<Renter>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });
            using (NativeArray<Entity> es = q.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity c in es)
                {
                    LodgingProvider lp = EntityManager.GetComponentData<LodgingProvider>(c);
                    int r = EntityManager.GetBuffer<Renter>(c, true).Length;
                    companies++;
                    rented += r;
                    rooms += r + lp.m_FreeRooms;
                    if (mergedType == null || !EntityManager.HasComponent<PropertyRenter>(c))
                        continue;
                    Entity b = EntityManager.GetComponentData<PropertyRenter>(c).m_Property;
                    if (b == Entity.Null || !EntityManager.Exists(b) || !EntityManager.HasComponent(b, ComponentType.ReadOnly(mergedType)))
                        continue;
                    string name = EntityManager.HasComponent<PrefabRef>(b) ? m_Prefabs.GetPrefabName(EntityManager.GetComponentData<PrefabRef>(b).m_Prefab) : "?";
                    merged.Add(new JObject { ["building"] = $"{b.Index}:{b.Version}", ["prefab"] = name, ["rented"] = r, ["rooms"] = r + lp.m_FreeRooms, ["price"] = lp.m_Price });
                }
            }
            q.Dispose();
            return new JObject { ["companies"] = companies, ["rented"] = rented, ["rooms"] = rooms, ["merged"] = merged };
        }

        private PropertyInfo TourBonusProperty(out object settings)
        {
            settings = null;
            Type modType = FindTypeByName("SkiResortMod.Mod");
            settings = modType?.GetProperty("Settings", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
            return settings?.GetType().GetProperty("ResortTourismBonusPct") ?? settings?.GetType().GetProperty("ResortAttractionDevPct");
        }

        /// <summary>ResortTourismBonusSystem's internal state (1.0.2+): applied, cap, sum, day mean. Null on an older build.</summary>
        private JObject TourBonusSystemState()
        {
            Type t = FindTypeByName("SkiResortMod.Systems.ResortTourismBonusSystem");
            if (t == null)
                return null;
            object sys = World.GetExistingSystemManaged(t);
            if (sys == null)
                return null;
            const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            JToken Read(string name) { object v = t.GetProperty(name, F)?.GetValue(sys); return v == null ? null : JToken.FromObject(v); }
            return new JObject { ["applied"] = Read("AppliedPct"), ["cap"] = Read("CapPct"), ["sum"] = Read("SumPct"), ["dayMean"] = Read("TouristsDayMean") };
        }

        /// <summary>One city statistic: GetStatisticValue, its prefab's collection type and the last <paramref name="last"/> samples
        /// (m_TotalValue, newest last).</summary>
        private JObject TourStatistic(StatisticType type, int last)
        {
            CityStatisticsSystem stats = World.GetOrCreateSystemManaged<CityStatisticsSystem>();
            var o = new JObject { ["value"] = stats.GetStatisticValue(type) };
            EntityQuery q = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<StatisticsData>());
            using (NativeArray<Entity> es = q.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity e in es)
                {
                    StatisticsData d = EntityManager.GetComponentData<StatisticsData>(e);
                    if (d.m_StatisticType == type)
                        o["collection"] = d.m_CollectionType.ToString();
                }
            }
            q.Dispose();
            if (last > 0)
            {
                NativeArray<long> data = stats.GetStatisticDataArrayLong(type);
                var arr = new JArray();
                for (int i = Math.Max(0, data.Length - last); i < data.Length; i++)
                    arr.Add(data[i]);
                o["samples"] = arr;
                o["sampleCount"] = data.Length;
            }
            return o;
        }

        private JObject TourIncome()
        {
            if (m_City.City == Entity.Null)
                throw new InvalidOperationException("no city loaded");
            return new JObject
            {
                ["frame"] = m_Simulation.frameIndex,
                ["time"] = TimeState(),
                ["touristIncome"] = TourStatistic(StatisticType.TouristIncome, 40),
                ["touristCount"] = TourStatistic(StatisticType.TouristCount, 40),
                ["tourismOverhaul"] = AppDomain.CurrentDomain.GetAssemblies().Any(asm => asm.GetName().Name.IndexOf("TourismOverhaul", StringComparison.OrdinalIgnoreCase) >= 0),
                ["bonusSystem"] = TourBonusSystemState(),
            };
        }

        private JToken TourBonusValue()
        {
            PropertyInfo p = TourBonusProperty(out object s);
            return p == null ? (JToken)"absent (published build)" : new JValue(Convert.ToDouble(p.GetValue(s)));
        }

        private JObject TourBonus(JObject args)
        {
            PropertyInfo p = TourBonusProperty(out object s) ?? throw new InvalidOperationException("this SkiResortMod build has neither ResortTourismBonusPct nor ResortAttractionDevPct");
            double before = Convert.ToDouble(p.GetValue(s));
            if (args["pct"] != null)
            {
                p.SetValue(s, Convert.ChangeType((double)args["pct"], p.PropertyType));
                Mod.Log.Info($"[tourism] {p.Name} {before} -> {p.GetValue(s)}");
            }
            return new JObject { ["setting"] = p.Name, ["before"] = before, ["now"] = Convert.ToDouble(p.GetValue(s)) };
        }
    }
}
