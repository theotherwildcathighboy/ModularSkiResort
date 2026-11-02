// Merge an existing building into a ski area (MergeRules, LodgeIdentity). MI1-MI4 the identity regression: every catalog lodge resolves to
// its own row, tagged or not, exactly as SkiCatalog.LodgeByPrefab; MK1-MK12 the kind classification; MS1-MS6 size gates and tier lowering;
// MF1-MF8 cost parity; MJ1-MJ5 carried jobs; MA1-MA5 the attraction combine; MT1-MT8 states; MD1-MD3 the duplicate guard.

using System;
using System.Collections.Generic;
using SkiResortMod.Systems;

namespace BuildingBatch
{
    internal static class MergeTests
    {
        public static void Run(Func<bool, string, string, bool> check)
        {
            IReadOnlyList<LodgeDef> lodges = SkiCatalog.Lodges;
            check(lodges.Count == 5, "MI0", "the catalog has 5 lodge tiers (" + lodges.Count + ")");

            // MI1-MI4: our own lodges behave exactly as before. The resolver returns the catalog row for our prefab whatever a tag says.
            bool same = true, tagIgnored = true;
            for (int i = 0; i < lodges.Count; i++)
            {
                LodgeDef row = SkiCatalog.LodgeByPrefab(lodges[i].Prefab);
                same &= ReferenceEquals(LodgeIdentity.Resolve(row, false, -1, lodges), row) && row != null && row.Index == i;
                for (int t = -1; t <= lodges.Count; t++)
                    tagIgnored &= ReferenceEquals(LodgeIdentity.Resolve(row, true, t, lodges), row);
            }
            check(same, "MI1", "every catalog lodge resolves to its own row (LodgeByPrefab)");
            check(tagIgnored, "MI2", "a tag on our own lodge never changes its row");
            bool tagged = true;
            for (int t = 0; t < lodges.Count; t++)
                tagged &= ReferenceEquals(LodgeIdentity.Resolve(null, true, t, lodges), lodges[t]);
            check(tagged, "MI3", "a tagged building resolves to the tier it names");
            check(LodgeIdentity.Resolve(null, false, 0, lodges) == null && LodgeIdentity.Resolve(null, true, -1, lodges) == null &&
                  LodgeIdentity.Resolve(null, true, lodges.Count, lodges) == null && SkiCatalog.LodgeByPrefab("CityPark01") == null,
                  "MI4", "untagged, bad tier or a non-catalog prefab resolves to nothing");

            // MK: kinds.
            var basic = new MergeRules.Facts { HasBuilding = true, LotCells = 100 };
            Kind(check, "MK1", "plain decor", basic, MergeRules.Kind.Decor, MergeRules.Refusal.None);
            var f = basic; f.Park = true; f.Service = true;
            Kind(check, "MK2", "P&R park (Park wins over service)", f, MergeRules.Kind.Park, MergeRules.Refusal.None);
            f = basic; f.Signature = true; f.Spawnable = true;
            Kind(check, "MK3", "residential / commercial signature", f, MergeRules.Kind.Signature, MergeRules.Refusal.None);
            f.IndustrialZone = true;
            Kind(check, "MK4", "industrial signature refused", f, MergeRules.Kind.None, MergeRules.Refusal.Industrial);
            f = basic; f.Spawnable = true;
            Kind(check, "MK5", "growable refused", f, MergeRules.Kind.None, MergeRules.Refusal.Growable);
            f = basic; f.Service = true;
            Kind(check, "MK6", "other city service refused", f, MergeRules.Kind.None, MergeRules.Refusal.Service);
            f = basic; f.Company = true;
            Kind(check, "MK7", "company / extractor refused", f, MergeRules.Kind.None, MergeRules.Refusal.Company);
            f = basic; f.Park = true; f.HasOwner = true;
            Kind(check, "MK8", "sub-building or upgrade refused", f, MergeRules.Kind.None, MergeRules.Refusal.SubBuilding);
            f = basic; f.Park = true; f.Ours = true;
            Kind(check, "MK9", "our own prefab refused", f, MergeRules.Kind.None, MergeRules.Refusal.Ours);
            f = basic; f.Park = true; f.Transient = true;
            Kind(check, "MK10", "transient state refused", f, MergeRules.Kind.None, MergeRules.Refusal.Transient);
            f = new MergeRules.Facts { LotCells = 100 };
            Kind(check, "MK11", "not a building refused", f, MergeRules.Kind.None, MergeRules.Refusal.NotBuilding);
            f = basic; f.Park = true; f.LotCells = 2;
            Kind(check, "MK12", "a 2x1 pocket park is too small", f, MergeRules.Kind.None, MergeRules.Refusal.TooSmall);

            // MS: size gates (aFrame 9, hut 4, mid 16, summit 16, grand 48).
            check(MergeRules.SmallestLot == 4, "MS1", "the smallest lot is the hut's 4 cells");
            check(MergeRules.AllowedMask(4, 5) == 0b00010, "MS2", "2x2 (Valmerin small): hut only");
            check(MergeRules.AllowedMask(9, 5) == 0b00011, "MS3", "3x3: hut and A-frame");
            check(MergeRules.AllowedMask(25, 5) == 0b01111, "MS4", "5x5: up to Chalet and Summit");
            check(MergeRules.AllowedMask(26 * 24, 5) == 0b11111, "MS5", "OldCastle01 26x24: every tier");
            check(MergeRules.BestAllowed(4, 25, lodges) == 3 && MergeRules.BestAllowed(2, 9, lodges) == 0 &&
                  MergeRules.BestAllowed(0, 4, lodges) == 1 && MergeRules.BestAllowed(1, 3, lodges) == -1 && MergeRules.BestAllowed(2, 30, lodges) == 2,
                  "MS6", "a lot that shrank lowers the tier to the best one below that fits (Grand on 5x5 -> Summit; Chalet on 3x3 -> A-frame)");

            // MF: cost parity, the research doc's examples.
            LodgeDef aFrame = lodges[0], mid = lodges[2], grand = lodges[4];
            check(MergeRules.MergeFee(aFrame.BuildCost, 0) == 40000 &&
                  MergeRules.MonthlyFee(aFrame.Upkeep, MergeRules.OwnCityUpkeep(MergeRules.Kind.Signature, 5000)) == 3000,
                  "MF1", "a signature as an A-frame: 40,000 once, 3,000 a month (renters pay its own upkeep)");
            check(MergeRules.MergeFee(grand.BuildCost, 4800000) == 0 &&
                  MergeRules.MonthlyFee(grand.Upkeep, MergeRules.OwnCityUpkeep(MergeRules.Kind.Park, 240000)) == 0,
                  "MF2", "OldCastle01 as a Grand lodge: nothing once, nothing a month");
            check(MergeRules.MergeFee(mid.BuildCost, 56000) == 144000 &&
                  MergeRules.MonthlyFee(mid.Upkeep, MergeRules.OwnCityUpkeep(MergeRules.Kind.Park, 12500)) == 0,
                  "MF3", "CityPark04 as a Chalet: 144,000 once, 0 a month");
            check(MergeRules.MergeFee(grand.BuildCost, 42000) == 1158000 &&
                  MergeRules.MonthlyFee(grand.Upkeep, MergeRules.OwnCityUpkeep(MergeRules.Kind.Park, 8500)) == 11500,
                  "MF4", "CityPark03 as a Grand lodge: 1,158,000 once, 11,500 a month");
            check(MergeRules.OwnCityUpkeep(MergeRules.Kind.Decor, 900) == 0, "MF5", "decor costs the city nothing of its own");
            check(MergeRules.TierChangeCharge(1200000, 40000) == 1160000, "MF6", "a tier up charges the difference to what was paid");
            check(MergeRules.TierChangeCharge(40000, 1200000) == 0, "MF7", "a tier down refunds nothing");
            check(MergeRules.MergeFee(40000, -5) == 40000 && MergeRules.MonthlyFee(3000, -1) == 3000, "MF8", "negative own costs read as 0");

            // MJ: carried jobs.
            check(MergeRules.TierJobs(lodges[0]) == 4 && MergeRules.TierJobs(lodges[1]) == 4 && MergeRules.TierJobs(lodges[2]) == 12 &&
                  MergeRules.TierJobs(lodges[3]) == 8 && MergeRules.TierJobs(lodges[4]) == 18, "MJ1", "tier jobs 4, 4, 12, 8, 18");
            check(MergeRules.CarriedJobs(grand, 30) == 0, "MJ2", "OldCastle01 (30 own) as Grand carries 0");
            check(MergeRules.CarriedJobs(aFrame, 0) == 4, "MJ3", "a signature as an A-frame carries 4");
            check(MergeRules.CarriedJobs(mid, 5) == 7, "MJ4", "a park with 5 own jobs as a Chalet carries 7");
            check(MergeRules.CarriedJobs(null, 0) == 0, "MJ5", "no row, no jobs");

            // MA: attraction.
            check(MergeRules.Combine(185, 100) == 210, "MA1", "LandhausDresden 185 with a share of 100 writes 210 (the P0 probe's value)");
            check(MergeRules.Combine(0, 37) == 37 && MergeRules.Combine(37, 0) == 37, "MA2", "either part alone passes through");
            check(MergeRules.Combine(-4, 3) == 3, "MA3", "a negative V reads as 0");
            double v = 185, p = 100, c = MergeRules.Combine(185, 100);
            check(Math.Abs(c * c - (v * v + p * p)) <= 2 * c + 1, "MA4", "a^2 = V^2 + P^2 within rounding: own tourism kept, the share added");
            check(MergeRules.IsOwnSlice(9, 9) && !MergeRules.IsOwnSlice(10, 9), "MA5", "V is read only on the building's own slice");

            // MT: states.
            check(MergeRules.StateOf(false, true, false, false, true, true) == MergeRules.State.Unknown, "MT1", "unknown before the first recompute");
            check(MergeRules.StateOf(true, false, false, false, true, true) == MergeRules.State.AssetMissing, "MT2", "a prefab that no longer qualifies");
            check(MergeRules.StateOf(true, true, true, false, true, true) == MergeRules.State.Destroyed, "MT3", "destroyed");
            check(MergeRules.StateOf(true, true, false, false, false, true) == MergeRules.State.NoArea, "MT4", "no sign within the tether");
            check(MergeRules.StateOf(true, true, false, false, true, false) == MergeRules.State.TooFar, "MT5", "too far from the runs");
            check(MergeRules.StateOf(true, true, false, true, true, true) == MergeRules.State.Inactive, "MT6", "switched off");
            check(MergeRules.StateOf(true, true, false, false, true, true) == MergeRules.State.Counted, "MT7", "counted");
            check(MergeRules.InArea(MergeRules.State.Inactive) && !MergeRules.Billed(MergeRules.State.Inactive) &&
                  MergeRules.Billed(MergeRules.State.Counted) && !MergeRules.InArea(MergeRules.State.TooFar), "MT8",
                  "an Inactive one is listed but not billed; a far one neither");

            // MD: duplicates and serials.
            check(MergeRules.IsDuplicateLoser(new EntityRef(20, 1), new EntityRef(10, 1)) &&
                  !MergeRules.IsDuplicateLoser(new EntityRef(10, 1), new EntityRef(20, 1)), "MD1", "the later entity loses the serial");
            check(MergeRules.NextSerial(0) == 1 && MergeRules.NextSerial(7) == 8, "MD2", "serials count up from 1");
            check(MergeRules.NextSerial(int.MaxValue) == int.MaxValue, "MD3", "the serial saturates");
        }

        private static void Kind(Func<bool, string, string, bool> check, string id, string what, MergeRules.Facts f,
                                 MergeRules.Kind kind, MergeRules.Refusal refusal)
        {
            MergeRules.Kind k = MergeRules.Classify(f, out MergeRules.Refusal r);
            check(k == kind && r == refusal, id, what + " (" + k + ", " + r + ")");
        }
    }
}
