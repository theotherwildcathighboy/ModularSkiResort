// Pure rules of merging an existing building into a ski area as a lodge (docs/research/merge-asset-into-resort-2026-11-02.md). The
// building keeps its own prefab and vanilla behavior; a saved tag (SkiMergedLodge) names a lodge tier, and that tier's catalog row then
// supplies score, skier room, jobs and attraction share as if the player had built the tier there. ADD ALONGSIDE, NEVER STRIP what
// vanilla cannot give back: the building's own WorkProvider, an AttractivenessProvider that came with its prefab, renters, prefab flags.
// - Kinds: Parks & Recreation buildings with a Park component (parks, landmarks), residential / commercial / mixed / office signatures,
//   and plain decor buildings (no service, no company). Refused: growables (they level up, empty out, get condemned:
//   ZoneCheckSystem.cs:297-313, BuildingUpkeepSystem.cs:721-730), industrial signatures, other city services, sub-buildings and upgrades
//   (Owner), company and extractor buildings, our own prefabs, and transient states.
// - Size: a tier needs a lot of at least MinCells (first values, not tuned).
// - Cost parity: once max(0, tier build cost - own construction cost); monthly max(0, tier upkeep - own city-billed upkeep). A
//   signature's upkeep is paid by its renters (BuildingUpkeepSystem.cs:175-226), so its city-billed upkeep is 0. No refund.
// - Jobs: the tier's jobs (Workplace + sign-carried) less the building's own WorkplaceData jobs ride on the area's job carrier.
// - Attraction: round(sqrt(V^2 + P^2)), V = vanilla's own value read on its update slice, P = the lodge share (PerLodge): the building
//   keeps its own tourism (TourismSystem.cs:95-107 sums a^2) and the area's lodge total does not grow with more lodges.
// - Counted: linked to a live Welcome Sign (nearest within the tether) AND within SkiDayRules.SlopesideMaxM of a run or lift station of
//   that area, re-checked every recompute, never sticky. Not a guest destination in v1.
// Pure file: System and System.Collections.Generic only (+ SkiCatalog's LodgeDef). Linked into tests/BuildingBatch.

using System;
using System.Collections.Generic;

namespace SkiResortMod.Systems
{
    public static class MergeRules
    {
        /// <summary>What a building can be merged as. None = refused (see Refusal).</summary>
        public enum Kind : byte { None = 0, Park = 1, Signature = 2, Decor = 3 }

        /// <summary>Why a building cannot be merged, first failing test wins (Classify). Appended only: the hub reads the number.</summary>
        public enum Refusal : byte
        {
            None = 0,
            NotBuilding = 1,
            Ours = 2,
            SubBuilding = 3,
            Transient = 4,
            Growable = 5,
            Industrial = 6,
            Company = 7,
            Service = 8,
            TooSmall = 9,
        }

        /// <summary>The state of a merged building, for the hub row and the card. Appended only.</summary>
        public enum State : byte
        {
            Counted = 0,
            NoArea = 1,        // no live Welcome Sign within the tether
            TooFar = 2,        // linked, but farther than SlopesideMaxM from every run and lift station of the area
            AssetMissing = 3,  // its prefab no longer qualifies (an obsolete placeholder, or a changed asset): kept, not counted
            Inactive = 4,      // the building is switched off
            Destroyed = 5,
            Unknown = 6,       // before the first recompute after a load
        }

        /// <summary>Facts about one building, read on the main thread from its components and its prefab's.</summary>
        public struct Facts
        {
            public bool HasBuilding;      // Game.Buildings.Building and a BuildingData prefab
            public bool Ours;             // one of our catalog prefabs (lodges, sign, school, patrol, depot)
            public bool HasOwner;         // a sub-building or service upgrade (Owner)
            public bool Transient;        // Temp, Deleted, Destroyed, Abandoned, Condemned
            public bool Signature;        // SignatureBuildingData on the prefab
            public bool Spawnable;        // SpawnableBuildingData on the prefab (a growable unless Signature)
            public bool IndustrialZone;   // the spawnable's zone is industrial (ZoneData.m_AreaType Industrial)
            public bool Company;          // CompanyObject / ExtractorFacility / a company spawn
            public bool Park;             // Game.Buildings.Park on the building (Parks & Recreation)
            public bool Service;          // CityServiceBuilding / ServiceObject without Park
            public int LotCells;          // BuildingData.m_LotSize x * y
        }

        // First values by tier index (SkiCatalog.Lodges: 0 aFrame, 1 hut, 2 mid, 3 summit, 4 grand). Our own lots are 30, 9, 20, 30, 90.
        private static readonly int[] s_MinCells = { 9, 4, 16, 16, 48 };

        /// <summary>The smallest lot (cells) a tier needs; int.MaxValue for an unknown tier.</summary>
        public static int MinCells(int tier)
        {
            return tier >= 0 && tier < s_MinCells.Length ? s_MinCells[tier] : int.MaxValue;
        }

        /// <summary>The smallest MinCells of any tier (the Mountain Hut's 4): below it nothing can be merged.</summary>
        public static int SmallestLot
        {
            get
            {
                int min = int.MaxValue;
                for (int i = 0; i < s_MinCells.Length; i++)
                    min = Math.Min(min, s_MinCells[i]);
                return min;
            }
        }

        /// <summary>The kind, or None with the refusal. Order: not a building, ours, sub-building, transient, signature (industrial refused),
        /// growable, company, park, other service, then plain decor; last the size against the smallest tier.</summary>
        public static Kind Classify(in Facts f, out Refusal refusal)
        {
            refusal = Refusal.None;
            Kind kind;
            if (!f.HasBuilding)
                refusal = Refusal.NotBuilding;
            else if (f.Ours)
                refusal = Refusal.Ours;
            else if (f.HasOwner)
                refusal = Refusal.SubBuilding;
            else if (f.Transient)
                refusal = Refusal.Transient;
            if (refusal != Refusal.None)
                return Kind.None;
            if (f.Signature)
            {
                if (f.IndustrialZone)
                {
                    refusal = Refusal.Industrial;
                    return Kind.None;
                }
                kind = Kind.Signature;
            }
            else if (f.Spawnable)
            {
                refusal = Refusal.Growable;
                return Kind.None;
            }
            else if (f.Company)
            {
                refusal = Refusal.Company;
                return Kind.None;
            }
            else if (f.Park)
                kind = Kind.Park;
            else if (f.Service)
            {
                refusal = Refusal.Service;
                return Kind.None;
            }
            else
                kind = Kind.Decor;
            if (f.LotCells < SmallestLot)
            {
                refusal = Refusal.TooSmall;
                return Kind.None;
            }
            return kind;
        }

        /// <summary>The tier fits the lot.</summary>
        public static bool TierAllowed(int tier, int lotCells)
        {
            return lotCells >= MinCells(tier);
        }

        /// <summary>Bit i set = tier i allowed by the lot, for <paramref name="tiers"/> tiers.</summary>
        public static int AllowedMask(int lotCells, int tiers)
        {
            int mask = 0;
            for (int i = 0; i < tiers && i < 31; i++)
            {
                if (TierAllowed(i, lotCells))
                    mask |= 1 << i;
            }
            return mask;
        }

        /// <summary>The tier kept after the lot changed (a prefab swap): the same tier if it still fits, else the allowed tier with the highest
        /// base B below it (ties to the lower index), else -1 (nothing fits).</summary>
        public static int BestAllowed(int tier, int lotCells, IReadOnlyList<LodgeDef> lodges)
        {
            if (tier >= 0 && tier < lodges.Count && TierAllowed(tier, lotCells))
                return tier;
            double cap = tier >= 0 && tier < lodges.Count ? lodges[tier].B : double.MaxValue;
            int best = -1;
            for (int i = 0; i < lodges.Count; i++)
            {
                if (!TierAllowed(i, lotCells) || lodges[i].B > cap)
                    continue;
                if (best < 0 || lodges[i].B > lodges[best].B)
                    best = i;
            }
            return best;
        }

        /// <summary>The one-time fee: the tier's build cost minus what the building already cost.</summary>
        public static int MergeFee(int tierBuildCost, int ownConstructionCost)
        {
            return Math.Max(0, tierBuildCost - Math.Max(0, ownConstructionCost));
        }

        /// <summary>The monthly fee while counted: the tier's upkeep minus what the city already pays for the building.</summary>
        public static int MonthlyFee(int tierUpkeep, int ownCityUpkeep)
        {
            return Math.Max(0, tierUpkeep - Math.Max(0, ownCityUpkeep));
        }

        /// <summary>What the city pays for the building itself: a park's own upkeep; a signature's renters pay theirs, decor has none.</summary>
        public static int OwnCityUpkeep(Kind kind, int prefabUpkeep)
        {
            return kind == Kind.Park ? Math.Max(0, prefabUpkeep) : 0;
        }

        /// <summary>A tier change charges the new fee less what was paid so far; never a refund.</summary>
        public static int TierChangeCharge(int newFee, int feePaid)
        {
            return Math.Max(0, newFee - Math.Max(0, feePaid));
        }

        /// <summary>All the jobs a tier brings: its own Workplace plus the sign-carried ones (A-frame 4, Hut 4, Chalet 12, Summit 8, Grand 18).</summary>
        public static int TierJobs(LodgeDef def)
        {
            return def == null ? 0 : Math.Max(0, def.Jobs) + Math.Max(0, def.SignJobs);
        }

        /// <summary>The jobs the carrier takes for a merged building: the tier's less the building's own workplaces.</summary>
        public static int CarriedJobs(LodgeDef def, int ownWorkplaces)
        {
            return Math.Max(0, TierJobs(def) - Math.Max(0, ownWorkplaces));
        }

        /// <summary>round(sqrt(V^2 + P^2)) with negatives read as 0, capped at int.MaxValue.</summary>
        public static int Combine(int vanilla, int share)
        {
            double v = Math.Max(0, vanilla), p = Math.Max(0, share);
            double c = Math.Round(Math.Sqrt(v * v + p * p));
            return c >= int.MaxValue ? int.MaxValue : (int)c;
        }

        /// <summary>The resort's part of a merged building's tourism term: P^2 / 10^4 (the game's a^2 / 10^4 per provider,
        /// TourismSystem.cs:101, with a^2 = V^2 + P^2 by Combine). Its own V stays the city's. 0 for no write or a negative P.</summary>
        public static double ShareTerm(int share)
        {
            double p = Math.Max(0, share);
            return p * p / 10000.0;
        }

        /// <summary>The tick's update slice is the building's own: vanilla AttractionSystem has just written its V
        /// (SimulationUtils.GetUpdateFrameWithInterval(frame, 16, 16) against UpdateFrame.m_Index, AttractionSystem.cs:236).</summary>
        public static bool IsOwnSlice(uint slice, uint ownFrame)
        {
            return slice == ownFrame;
        }

        /// <summary>The hub state of a merged building, first failing test wins.</summary>
        public static State StateOf(bool known, bool kindOk, bool destroyed, bool inactive, bool linked, bool slopeside)
        {
            if (!known)
                return State.Unknown;
            if (!kindOk)
                return State.AssetMissing;
            if (destroyed)
                return State.Destroyed;
            if (!linked)
                return State.NoArea;
            if (!slopeside)
                return State.TooFar;
            return inactive ? State.Inactive : State.Counted;
        }

        /// <summary>Counted = in the area's lodge set (score, capacity, jobs, attraction, fee); an Inactive one is listed but not active.</summary>
        public static bool InArea(State s)
        {
            return s == State.Counted || s == State.Inactive;
        }

        /// <summary>The monthly fee runs while the building is counted and switched on.</summary>
        public static bool Billed(State s)
        {
            return s == State.Counted;
        }

        /// <summary>Two buildings carrying one serial (a copy that kept our component): the one to untag is the later (Index, Version), so the
        /// older one stays merged.</summary>
        public static bool IsDuplicateLoser(EntityRef candidate, EntityRef keeper)
        {
            return candidate.CompareTo(keeper) > 0;
        }

        /// <summary>The next serial: one above the highest seen (1 when none).</summary>
        public static int NextSerial(int highestSeen)
        {
            return highestSeen < 1 ? 1 : highestSeen == int.MaxValue ? int.MaxValue : highestSeen + 1;
        }

        // The hub-armed merge overlay (SkiMergeOverlaySystem, P2.6)

        /// <summary>How the overlay tints a building near a ski area. Appended only.</summary>
        public enum OverlayTint : byte
        {
            None = 0,      // not drawn: a kind that can never be merged, or already merged
            Allowed = 1,   // green: at least one tier fits the lot
            TooSmall = 2,  // gray: the kind qualifies, but no tier fits the lot (AllowedMask 0)
        }

        /// <summary>The tint of one building from its Classify result and lot. Already merged buildings are not tinted (the Overview's
        /// Merged buildings fold lists them). A kind that qualifies only fails Classify on the lot (TooSmall), so the gray case is
        /// "allowed kind, AllowedMask 0", whichever of the two says it.</summary>
        public static OverlayTint TintOf(Kind kind, Refusal refusal, int allowedMask, bool merged)
        {
            if (merged)
                return OverlayTint.None;
            bool kindOk = kind != Kind.None || refusal == Refusal.TooSmall;
            if (!kindOk)
                return OverlayTint.None;
            return allowedMask != 0 && kind != Kind.None ? OverlayTint.Allowed : OverlayTint.TooSmall;
        }

        /// <summary>The overlay's armed state after one frame's events. The hub button sets <paramref name="request"/> (1 arm, 0 disarm,
        /// -1 none); Escape, a merge (or tier change) and a load always disarm, whatever the button said in the same frame. Arming needs a
        /// Welcome Sign in the city.</summary>
        public static bool NextArmed(bool armed, int request, bool escape, bool merged, bool loaded, bool hasSign)
        {
            if (loaded || escape || merged || !hasSign)
                return false;
            if (request == 1)
                return true;
            if (request == 0)
                return false;
            return armed;
        }

        /// <summary>The candidate list is rebuilt when the mode arms, when the areas' runs and lift stations moved (the slopeside
        /// geometry's revision), when the city's building count changed, or on request (an unmerge makes a new candidate). Never while
        /// disarmed.</summary>
        public static bool OverlayRebuild(bool armed, bool justArmed, int geometryRevision, int builtGeometryRevision,
                                          int buildingCount, int builtBuildingCount, bool requested)
        {
            if (!armed)
                return false;
            return justArmed || requested || geometryRevision != builtGeometryRevision || buildingCount != builtBuildingCount;
        }
    }

    /// <summary>The one answer to "which lodge tier is this building?": the catalog row of our prefab, else the tier a SkiMergedLodge tag
    /// names. Our prefabs always resolve to their own row, tag or not, so our lodges behave exactly as before the merge feature.</summary>
    public static class LodgeIdentity
    {
        /// <summary><paramref name="catalogRow"/> when non-null; else lodges[tier] for a tagged building with a valid tier; else null.</summary>
        public static LodgeDef Resolve(LodgeDef catalogRow, bool tagged, int tier, IReadOnlyList<LodgeDef> lodges)
        {
            if (catalogRow != null)
                return catalogRow;
            if (!tagged || lodges == null || tier < 0 || tier >= lodges.Count)
                return null;
            return lodges[tier];
        }
    }
}
