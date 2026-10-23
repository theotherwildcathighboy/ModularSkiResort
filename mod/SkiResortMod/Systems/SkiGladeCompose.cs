// The glade tool's apply composition: one tree-density level per layer (Level 1..8) for the run interior or the tree line. An
// apply brings the layer to the level's tree count by planting, removing or felling, never both planting and removing / felling.
// Pure on purpose: usings are System, System.Collections.Generic and Unity.Mathematics only, so it also links into the net8 host tests/GladeBatch.
// Contract: Compose(input, output[, cache]) is a pure function of `input` (a cache never changes the result). No static mutable
// state, no Dictionary/HashSet iteration feeds the output, never logs, never throws on bad geometry or parameters; runs on re-plan
// only (never per frame) and allocates. Params.MinSpacing must be the target layer's spacing; Mode, ThinFraction, GladeDensity and
// MaxPlant are not read (Compose sets them).
// Engine notes: EngineNotes/SkiGladeCompose.md

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>One of THIS run's member trees, as the census found it </summary>
    public struct ComposeMember
    {
        public float3 Position;
        public byte Layer, Variant, PlanGen;
    }

    /// <summary>Per-apply caps (the tool passes Tuning.MaxPlantPerApply,
    /// MaxThinPerApply, MaxRemovePerApply). Negative values act as 0.</summary>
    public struct ComposeCaps
    {
        public int MaxPlant, MaxFell, MaxRemove;
    }

    /// <summary>Everything the composition depends on. Arrays are
    /// read, never written.</summary>
    public sealed class ComposeInput
    {
        public float3[] Ring;
        public int3[] Triangles;
        /// <summary>Spacing (of the TARGET layer), margins, belt shape, species, ages,
        /// AttemptsPerTree. Mode, ThinFraction, GladeDensity and MaxPlant are not read.</summary>
        public GladeParams Params;
        /// <summary>Tree density level 1..8 (clamped; 0 reads as 1 = Clear).</summary>
        public byte Level;
        /// <summary>False = run interior (layer 0), true = tree line (layer 1).</summary>
        public bool TreeLine;
        /// <summary>Layout variant of the stream; always 0 for new plantings.</summary>
        public byte Variant;
        public GladeObstacle[] Obstacles;
        public float3[][] ExclusionRings;
        /// <summary>Census minus this run's members: wild inside, band, foreign (other runs'
        /// members, owned trees). Parallel to Thinnable.</summary>
        public float3[] Existing;
        /// <summary>Wild trees the tool may fell (null = none).</summary>
        public bool[] Thinnable;
        /// <summary>This run's members, both layers.</summary>
        public ComposeMember[] Own;
        /// <summary>Slot indices of the stream known to be validation-blocked (null = none).</summary>
        public HashSet<int> SkipSlots;
        public ComposeCaps Caps;
    }

    /// <summary>Compose output. Reusable: Clear()
    /// resets every field; the lists keep their capacity.</summary>
    public sealed class ComposeResult
    {
        /// <summary>P: the first Slice slots of the plan stream (slot id = index).</summary>
        public readonly GladePlan Plan = new GladePlan();
        /// <summary>Indices into Existing, ascending (wild trees felled by rank).</summary>
        public readonly List<int> Fell = new List<int>();
        /// <summary>Indices into Plan.Planted (= slot ids), ascending.</summary>
        public readonly List<int> Plant = new List<int>();
        /// <summary>Indices into Own: Other members first (other-tagged and misplaced, ascending
        /// x, z, index), then count-rule removals from the end of the keep order (highest slot
        /// first).</summary>
        public readonly List<int> Remove = new List<int>();
        /// <summary>LayerGlade (interior) / LayerBelt (tree line); LayerNone before a compose.</summary>
        public byte Layer;

        /// <summary>T, N, W, X, M at compose time (tree line: W = X = 0, N = M).</summary>
        public int Target, Inside, WildInside, FixedInside, OwnInside;
        /// <summary>Members kept by the count rule (= OwnInside when N &lt;= T).</summary>
        public int Keep;

        /// <summary>RawTarget = the stream's own (planner) target: interior round(StreamDensityPerM2
        /// · Area) − W − X, tree line B; Slice = |P|; FellFull = the count rule's wild fell before
        /// the cap (0 unless N &gt; T with no member kept); Same = current members of the target
        /// layer in its region (= SameIn = OwnInside); Other = members removed first of all
        /// (OtherTagged + Misplaced); Matched = matched slots of P; Missing = unmatched slots of
        /// P not skipped; Skipped = |SkipSlots ∩ P|; Deficit = max(0, T − N).</summary>
        public int RawTarget, Slice, FellFull, Same, Other, Matched, Missing, Skipped, Deficit;
        public bool Clamped, Saturated, Complete;
        public GladeStatus Status;
        /// <summary>FNV-1a 64 of everything P depends on (engine notes); never 0 once computed.</summary>
        public ulong PlanKey;

        /// <summary>The effective (clamped) level.</summary>
        public byte Level;
        /// <summary>Alias of OwnInside (current members in the layer region).</summary>
        public int SameIn;
        /// <summary>Members of the target layer with another variant or generation, in Other.</summary>
        public int OtherTagged;
        /// <summary>This run's members of either layer standing outside their own layer's region (engine notes: misplaced), in Other;
        /// removed first of all.</summary>
        public int Misplaced;
        /// <summary>In-region current members not on any slot of P (off the stream).</summary>
        public int OwnUnmatched;
        /// <summary>Plant branch: min(Deficit, MaxPlant) − |Plant| (blocked spots or no room).</summary>
        public int Shortfall;
        /// <summary>The slice asked of the planner (int.MaxValue = the full stream).</summary>
        public int RequestedSlice;
        /// <summary>P is a strict prefix of a longer stream (more slots follow).</summary>
        public bool PlanIsSlice;
        /// <summary>The requested slice ran out of attempts; P was re-planned in full and cut.</summary>
        public bool FullReplan;
        /// <summary>An in-region member was unmatched against a slice; P was re-planned in full
        /// to order the members (engine notes, PLAN STREAM).</summary>
        public bool FullForMembers;
        /// <summary>P came from the GladePlanCache.</summary>
        public bool CacheHit;
        /// <summary>Occupants handed to the planner / of which other-layer members / occupants
        /// hashed into PlanKey / of which other-layer members.</summary>
        public int Occupants, OccupantsOtherLayer, KeyedOccupants, KeyedOtherLayer;

        // ---- scratch (reused across composes; never part of the result) ----
        internal readonly GladePlan ProbeScratch = new GladePlan();
        internal readonly List<int> SameScratch = new List<int>();   // in-region current members only
        internal readonly List<int> OtherScratch = new List<int>();
        internal readonly List<int> WildScratch = new List<int>();
        internal readonly List<ulong> KeyScratch = new List<ulong>();
        internal readonly Dictionary<long, int> GridHead = new Dictionary<long, int>();
        internal int[] GridNext = new int[0];
        internal bool[] SlotMatched = new bool[0];
        internal int[] MemberSlot = new int[0];

        public void Clear()
        {
            Plan.Clear();
            Fell.Clear();
            Plant.Clear();
            Remove.Clear();
            Layer = SkiGladeCompose.LayerNone;
            Target = 0;
            Inside = 0;
            WildInside = 0;
            FixedInside = 0;
            OwnInside = 0;
            Keep = 0;
            RawTarget = 0;
            Slice = 0;
            FellFull = 0;
            Same = 0;
            Other = 0;
            Matched = 0;
            Missing = 0;
            Skipped = 0;
            Deficit = 0;
            Clamped = false;
            Saturated = false;
            Complete = false;
            Status = GladeStatus.Ok;
            PlanKey = 0UL;
            Level = 0;
            SameIn = 0;
            OtherTagged = 0;
            Misplaced = 0;
            OwnUnmatched = 0;
            Shortfall = 0;
            RequestedSlice = 0;
            PlanIsSlice = false;
            FullReplan = false;
            FullForMembers = false;
            CacheHit = false;
            Occupants = 0;
            OccupantsOtherLayer = 0;
            KeyedOccupants = 0;
            KeyedOtherLayer = 0;
        }
    }

    /// <summary>Small LRU of full or sliced plans keyed by PlanKey (engine notes, CACHE). Not thread
    /// safe (main thread only, like the tool). Entries own their storage (plans are copied).</summary>
    public sealed class GladePlanCache
    {
        private readonly ulong[] m_Keys;
        private readonly GladePlan[] m_Plans;
        private readonly bool[] m_IsSlice;
        private readonly bool[] m_Used;
        private readonly long[] m_Stamp;
        private long m_Clock;

        /// <summary>Lookups that reused / did not reuse an entry (plain counters for DUMP).</summary>
        public int Hits, Misses;

        public GladePlanCache(int capacity)
        {
            int n = capacity < 1 ? 1 : capacity;
            m_Keys = new ulong[n];
            m_Plans = new GladePlan[n];
            m_IsSlice = new bool[n];
            m_Used = new bool[n];
            m_Stamp = new long[n];
            for (int i = 0; i < n; i++)
                m_Plans[i] = new GladePlan();
        }

        public int Capacity => m_Keys.Length;

        public int Count
        {
            get
            {
                int c = 0;
                for (int i = 0; i < m_Used.Length; i++)
                {
                    if (m_Used[i])
                        c++;
                }
                return c;
            }
        }

        /// <summary>Drops every entry (call at load). Counters are kept.</summary>
        public void Clear()
        {
            for (int i = 0; i < m_Used.Length; i++)
            {
                m_Used[i] = false;
                m_Plans[i].Clear();
            }
        }

        /// <summary>An entry for `key` usable for a slice of `need` slots: not a slice, or a
        /// slice holding at least `need` slots.</summary>
        internal bool TryGet(ulong key, int need, out GladePlan plan, out bool isSlice)
        {
            for (int i = 0; i < m_Keys.Length; i++)
            {
                if (!m_Used[i] || m_Keys[i] != key)
                    continue;
                if (m_IsSlice[i] && m_Plans[i].Planted.Count < need)
                    break; // present but too short: recompute (Put replaces it)
                m_Stamp[i] = ++m_Clock;
                plan = m_Plans[i];
                isSlice = m_IsSlice[i];
                Hits++;
                return true;
            }
            plan = null;
            isSlice = false;
            Misses++;
            return false;
        }

        internal void Put(ulong key, GladePlan plan, bool isSlice)
        {
            int slot = -1;
            for (int i = 0; i < m_Keys.Length; i++)
            {
                if (m_Used[i] && m_Keys[i] == key)
                {
                    slot = i;
                    break;
                }
            }
            if (slot < 0)
            {
                long oldest = long.MaxValue;
                for (int i = 0; i < m_Keys.Length; i++)
                {
                    if (!m_Used[i])
                    {
                        slot = i;
                        break;
                    }
                    if (m_Stamp[i] < oldest)
                    {
                        oldest = m_Stamp[i];
                        slot = i;
                    }
                }
            }
            m_Keys[slot] = key;
            m_IsSlice[slot] = isSlice;
            m_Used[slot] = true;
            m_Stamp[slot] = ++m_Clock;
            SkiGladeCompose.CopyPlan(plan, m_Plans[slot], int.MaxValue);
        }
    }

    public static class SkiGladeCompose
    {
        /// <summary>Bump on any change to the planner, its salts, Seed32, VariantSalt, ThinSeed or a position-affecting Tuning
        /// value: every member then reads as Other and the next apply rebuilds the layer. 0 is reserved for v1 tags.</summary>
        public const byte PlanGeneration = 1;

        /// <summary>Seed of the rank key (ThinHash): a tree's rank depends only on its position.</summary>
        public const uint ThinSeed = 0x7A1C3E5Bu;

        /// <summary>Layer values (identical to SkiRunTreeMember.LayerGlade / LayerBelt; the tag system checks the equality at
        /// compile time). LayerNone = no compose.</summary>
        public const byte LayerGlade = 0, LayerBelt = 1, LayerNone = 255;

        /// <summary>m_Variant of a v1 tag (identical to SkiRunTreeMember.VariantUnknown).</summary>
        public const byte VariantUnknown = 0xFF;

        /// <summary>Density levels 1..Levels; the tool's default level.</summary>
        public const int Levels = 8;
        public const byte DefaultLevel = 4;

        /// <summary>Density of the interior plan stream, trees per m²: 1.10 × the level-8 density (157.3 vs 143 per ha), so a
        /// level-8 run replaces learned blocked spots from the stream's tail (engine notes, plan stream). A compile-time constant on
        /// purpose (no runtime float product: Mono and CoreCLR must read the same bits). Hashed into PlanKey via the stream params.</summary>
        public const float StreamDensityPerM2 = 157.3f / 10000f;

        /// <summary>Minimum float margin for SeparationHolds (m). Positions are floats: at a
        /// 14 km map offset one ulp is ~1 mm, so a pair distance can be off by ~2 mm; 1 cm
        /// keeps "an other-layer member can never block a slot" robust.</summary>
        public const float SeparationMarginM = 0.01f;

        // PlanKey relevance slack (m): the bound is exact in reals; this covers the raw ring used
        // here vs the planner's sanitized ring (vertices within 1 mm stripped) and rounding.
        private const double KeySlackM = 0.01;

        private const ulong FnvOffset64 = 14695981039346656037UL;
        private const ulong FnvPrime64 = 1099511628211UL;

        /// <summary>Per-variant salt for SkiGladePlanner.Seed32. Only variant 0 is planted.</summary>
        public static uint VariantSalt(int variant)
        {
            unchecked
            {
                return 0x5C1A9E5Du ^ ((uint)(variant + 1) * 0x9E3779B1u);
            }
        }

        /// <summary>Interior trees per m² (trees per hectare / 10,000) for level
        /// 1..8 (clamped): Clear 0, Scattered trees 6, Sparse 14, Open glade 28, Glade 45, Open
        /// forest 70, Forest 100, Thick forest 143.</summary>
        public static float InteriorDensity(int level)
        {
            switch (ClampLevel(level))
            {
                case 1: return 0f;
                case 2: return 6f / 10000f;
                case 3: return 14f / 10000f;
                case 4: return 28f / 10000f;
                case 5: return 45f / 10000f;
                case 6: return 70f / 10000f;
                case 7: return 100f / 10000f;
                default: return 143f / 10000f;
            }
        }

        /// <summary>Share of the tree-line target B for level 1..8 (clamped).</summary>
        public static float BeltFactor(int level)
        {
            switch (ClampLevel(level))
            {
                case 1: return 0f;
                case 2: return 0.15f;
                case 3: return 0.30f;
                case 4: return 0.45f;
                case 5: return 0.60f;
                case 6: return 0.75f;
                case 7: return 0.90f;
                default: return 1.00f;
            }
        }

        /// <summary>The rank key (see EngineNotes/SkiGladeCompose.md, ThinHash vs ThinSelect); 0 for a non-finite
        /// or out-of-range position.</summary>
        public static uint ThinHash(float3 position, uint seed)
        {
            return SkiGladePlanner.ThinHash(position, seed);
        }

        /// <summary>Layer separation: InnerMargin + BeltGap must exceed
        /// both MinSpacings by at least SeparationMarginM, so a correctly placed member of one
        /// layer can never block a slot of the other (d(inside, outside) >= d_in + d_out; the
        /// planner rejects only d &lt; MinSpacing). False on NaN/Inf.</summary>
        public static bool SeparationHolds(float innerMargin, float beltGap, float gladeMinSpacing, float beltMinSpacing)
        {
            float sum = innerMargin + beltGap;
            float need = math.max(gladeMinSpacing, beltMinSpacing);
            if (!math.isfinite(sum) || !math.isfinite(need))
                return false;
            return sum - need >= SeparationMarginM;
        }

        /// <summary>The PlanKey Compose would report for `input` (probe + key). Same value as
        /// ComposeResult.PlanKey after Compose(input, …). 0 for a null input or a degenerate
        /// ring.</summary>
        public static ulong PlanKey(ComposeInput input)
        {
            if (input == null)
                return 0UL;
            var scratch = new ComposeResult();
            float3[] occ;
            Prepare(input, scratch, out occ);
            return scratch.PlanKey;
        }

        public static GladeStatus Compose(ComposeInput input, ComposeResult output)
        {
            return Compose(input, output, null);
        }

        /// <summary>The count rule: EngineNotes/SkiGladeCompose.md. `cache` may be null.</summary>
        public static GladeStatus Compose(ComposeInput input, ComposeResult output, GladePlanCache cache)
        {
            if (output == null)
                output = new ComposeResult(); // never throw; the caller simply gets nothing back
            output.Clear();
            if (input == null)
            {
                output.Status = GladeStatus.TooFewVertices;
                return output.Status;
            }

            // Probe, counts, Same / Other split, occupants and the key.
            float3[] occupants;
            GladeStatus geo = Prepare(input, output, out occupants);
            if (geo != GladeStatus.Ok && geo != GladeStatus.Clamped)
            {
                output.Status = geo;
                return geo;
            }

            ComposeCaps caps = input.Caps;
            int capPlant = caps.MaxPlant > 0 ? caps.MaxPlant : 0;
            int capFell = caps.MaxFell > 0 ? caps.MaxFell : 0;
            int capRemove = caps.MaxRemove > 0 ? caps.MaxRemove : 0;

            bool interior = output.Layer == LayerGlade;
            int t = output.Target, n = output.Inside, w = output.WildInside, x = output.FixedInside;
            int own = output.OwnInside, others = output.Other;

            // 1. Other members first of all (any region).
            int othersRemoved = BuildRemoveOthers(input.Own, output, capRemove);
            int othersLeft = others - othersRemoved;

            // 2. P: a prefix of the level-independent stream.
            output.Deficit = n < t ? t - n : 0;
            bool plantBranch = n < t && others == 0;
            int want = plantBranch ? math.min(output.Deficit, capPlant) : 0;
            int skipCount = input.SkipSlots != null ? input.SkipSlots.Count : 0;
            int m = SatInt((long)output.Same + skipCount + want);
            GladeStatus ps = ComputeP(input, output, occupants, m, cache);
            if (ps != GladeStatus.Ok && ps != GladeStatus.Clamped)
                return Degenerate(output, ps); // cannot happen after the probe validated the ring

            bool removeTruncated = othersLeft > 0;
            bool fellTruncated = false;
            if (n > t)
            {
                // 3a. Too many: keep the first K in keep order, remove the rest, then fell.
                int keep = interior ? t - w - x : t;
                keep = keep < 0 ? 0 : keep > own ? own : keep;
                output.Keep = keep;
                if (output.OwnUnmatched > 0 && output.PlanIsSlice)
                {
                    ps = ComputeP(input, output, occupants, int.MaxValue, cache);
                    if (ps != GladeStatus.Ok && ps != GladeStatus.Clamped)
                        return Degenerate(output, ps);
                    output.FullForMembers = true;
                }
                int removeWanted = own - keep;
                int removed = BuildRemoveCountRule(input.Own, output, keep, capRemove - output.Remove.Count);
                if (removed < removeWanted)
                    removeTruncated = true;
                if (interior && keep == 0)
                {
                    int fellFull = w - math.max(0, t - x);
                    output.FellFull = fellFull > 0 ? fellFull : 0;
                    bool noMemberStands = othersLeft == 0 && removed == removeWanted;
                    if (noMemberStands && output.FellFull > 0)
                        BuildFell(input, output, math.min(output.FellFull, capFell));
                    fellTruncated = output.Fell.Count < output.FellFull;
                }
            }
            else
            {
                // 3b. Too few (or exact): plant the deficit; nothing removed by the count rule.
                output.Keep = own;
                if (plantBranch && want > 0)
                    BuildPlant(input, output, want);
            }
            output.Shortfall = plantBranch ? want - output.Plant.Count : 0;

            // 4. Flags.
            bool plantWaits = n < t && others > 0;
            bool plantMore = plantBranch && output.Deficit > output.Plant.Count &&
                             (output.PlanIsSlice || output.Missing > output.Plant.Count);
            output.Clamped = removeTruncated || fellTruncated || plantWaits || plantMore;
            output.Saturated = plantBranch && output.Shortfall > 0 && output.Plan.Saturated && !output.PlanIsSlice;
            output.Complete = output.Plant.Count == 0 && output.Remove.Count == 0 && output.Fell.Count == 0;
            output.Status = output.Clamped ? GladeStatus.Clamped : GladeStatus.Ok;
            return output.Status;
        }

        private static GladeStatus Degenerate(ComposeResult output, GladeStatus st)
        {
            output.Fell.Clear();
            output.Plant.Clear();
            output.Remove.Clear();
            output.Plan.Clear();
            output.Status = st;
            return st;
        }

        public static int ClampLevel(int level)
        {
            return level < 1 ? 1 : level > Levels ? Levels : level;
        }

        /// <summary>Engine notes, PROBE: geometry, T, the inside flags and W / X / M / Others; the
        /// occupants of P and PlanKey. Returns the geometry status.</summary>
        private static GladeStatus Prepare(ComposeInput input, ComposeResult output, out float3[] occupants)
        {
            byte layer = input.TreeLine ? LayerBelt : LayerGlade;
            bool interior = layer == LayerGlade;
            output.Layer = layer;
            int level = ClampLevel(input.Level);
            output.Level = (byte)level;
            occupants = Array.Empty<float3>();
            output.SameScratch.Clear();
            output.OtherScratch.Clear();
            output.WildScratch.Clear();
            output.KeyScratch.Clear();

            ComposeMember[] own = input.Own;
            int nOwn = own != null ? own.Length : 0;
            float3[] existing = input.Existing;
            int nEx = existing != null ? existing.Length : 0;

            // The probe: every position inside (planner test), T (interior) / B (tree line).
            var pos = new float3[nEx + nOwn];
            for (int i = 0; i < nEx; i++)
                pos[i] = existing[i];
            for (int j = 0; j < nOwn; j++)
                pos[nEx + j] = own[j].Position;
            GladeParams pt = input.Params;
            pt.Mode = GladeMode.Thin | (interior ? GladeMode.Glade : GladeMode.Belt);
            pt.ThinFraction = 1f;
            pt.GladeDensity = InteriorDensity(level);
            pt.MaxPlant = 0;
            GladePlan probe = output.ProbeScratch;
            GladeStatus geo = SkiGladePlanner.Plan(input.Ring, input.Triangles, ThinSeed, in pt, null, pos, null, null, probe);
            if (geo != GladeStatus.Ok && geo != GladeStatus.Clamped)
                return geo;
            var inside = new bool[pos.Length];
            List<int> thinned = probe.Thinned;
            for (int k = 0; k < thinned.Count; k++)
                inside[thinned[k]] = true;

            output.Target = interior ? probe.RawTargetGlade : RoundHalfUp((double)BeltFactor(level) * probe.RawTargetBelt);

            GladeParams p = input.Params;
            RingGeo ring = RingGeo.From(input.Ring);
            float minSpacing = SafeNonNeg(p.MinSpacing);
            double beltReach = (double)SafeNonNeg(p.BeltDepth) + minSpacing;

            // Existing: W (wild thinnable inside) and X (the rest inside).
            bool[] thin = input.Thinnable;
            int w = 0, x = 0;
            for (int i = 0; i < nEx; i++)
            {
                if (!inside[i])
                    continue;
                if (thin != null && i < thin.Length && thin[i])
                {
                    w++;
                    output.WildScratch.Add(i);
                }
                else
                {
                    x++;
                }
            }

            // Own (engine notes MISPLACED): the target layer splits into Same (current, in its region),
            // other-tagged and misplaced; the other layer into occupants (in their own region —
            // interior inside, tree line outside) and misplaced. Other = tagged ∪ misplaced, in Own
            // order; BuildRemoveOthers sorts them. A member with an unknown layer byte (never
            // written) stays an occupant and X when inside: nothing we cannot classify is removed.
            int otherLayer = 0, sameIn = 0, tagged = 0, misplaced = 0;
            bool[] occupantOwn = nOwn > 0 ? new bool[nOwn] : Array.Empty<bool>();
            for (int j = 0; j < nOwn; j++)
            {
                ComposeMember mbr = own[j];
                bool mIn = inside[nEx + j];
                if (mbr.Layer != layer)
                {
                    bool known = mbr.Layer == LayerGlade || mbr.Layer == LayerBelt;
                    bool outOfRegion = known && (mbr.Layer == LayerGlade ? !mIn : mIn);
                    if (outOfRegion)
                    {
                        output.OtherScratch.Add(j);
                        misplaced++;
                        continue;
                    }
                    occupantOwn[j] = true;
                    otherLayer++;
                    if (interior && mIn)
                        x++; // only an unknown-layer member reaches this (a known one inside is misplaced)
                    continue;
                }
                if (mbr.Variant != input.Variant || mbr.PlanGen != PlanGeneration)
                {
                    output.OtherScratch.Add(j);
                    tagged++;
                    continue;
                }
                bool region = interior ? mIn : !mIn && InBeltReach(ring, mbr.Position, beltReach);
                if (!region)
                {
                    output.OtherScratch.Add(j);
                    misplaced++;
                    continue;
                }
                output.SameScratch.Add(j);
                sameIn++;
            }
            if (!interior)
            {
                w = 0;
                x = 0;
                output.WildScratch.Clear(); // the tree line never fells
            }
            output.WildInside = w;
            output.FixedInside = x;
            output.OwnInside = sameIn;
            output.SameIn = sameIn;
            output.OtherTagged = tagged;
            output.Misplaced = misplaced;
            output.Inside = interior ? w + x + sameIn : sameIn;
            output.Same = output.SameScratch.Count;
            output.Other = output.OtherScratch.Count;

            // Occupants of P = every Existing tree ∪ this run's other-layer members in their own
            // region; the key hashes those that can affect P (engine notes, PLANKEY).
            occupants = new float3[nEx + otherLayer];
            List<ulong> keyed = output.KeyScratch;
            int wr = 0, keyedOther = 0;
            for (int i = 0; i < nEx; i++)
            {
                occupants[wr++] = existing[i];
                if (Matters(ring, existing[i], inside[i], layer, in p, minSpacing))
                    keyed.Add(XzBits(existing[i]));
            }
            for (int j = 0; j < nOwn; j++)
            {
                if (!occupantOwn[j])
                    continue;
                occupants[wr++] = own[j].Position;
                if (Matters(ring, own[j].Position, inside[nEx + j], layer, in p, minSpacing))
                {
                    keyed.Add(XzBits(own[j].Position));
                    keyedOther++;
                }
            }
            output.Occupants = occupants.Length;
            output.OccupantsOtherLayer = otherLayer;
            output.KeyedOccupants = keyed.Count;
            output.KeyedOtherLayer = keyedOther;
            output.PlanKey = HashKey(input, layer, keyed);
            return geo;
        }

        /// <summary>Engine notes, PLANKEY relevance. `inside` is the probe's (planner) flag.
        /// Conservative (true) whenever the ring or the position is unusable.</summary>
        private static bool Matters(RingGeo ring, float3 pos, bool inside, byte layer, in GladeParams p, float minSpacing)
        {
            if (!ring.Ok || !math.isfinite(pos.x) || !math.isfinite(pos.z))
                return true;
            if (layer == LayerGlade)
            {
                if (inside)
                    return true; // blocks slots and counts in the stream's raw target
                double t = (double)minSpacing - SafeNonNeg(p.InnerMargin) + KeySlackM;
                return math.sqrt(ring.BoundaryDistanceSq(new double2(pos.x, pos.z))) < t;
            }
            if (!inside)
                return true;
            double tb = (double)minSpacing - SafeNonNeg(p.BeltGap) + KeySlackM;
            return math.sqrt(ring.BoundaryDistanceSq(new double2(pos.x, pos.z))) < tb;
        }

        private static bool InBeltReach(RingGeo ring, float3 pos, double beltReach)
        {
            if (!ring.Ok || !math.isfinite(pos.x) || !math.isfinite(pos.z))
                return false;
            return ring.BoundaryDistanceSq(new double2(pos.x, pos.z)) <= beltReach * beltReach;
        }

        /// <summary>The planner params of the stream (engine notes, THE PLAN STREAM). MaxPlant is set
        /// by BuildP and never hashed.</summary>
        private static GladeParams StreamParams(ComposeInput input, byte layer)
        {
            GladeParams pp = input.Params;
            pp.Mode = layer == LayerGlade ? GladeMode.Glade : GladeMode.Belt;
            pp.ThinFraction = 0f;
            pp.GladeDensity = layer == LayerGlade ? StreamDensityPerM2 : 0f;   // stream density with headroom
            pp.MaxPlant = 0;
            return pp;
        }

        /// <summary>P = the first m slots of the stream, then matching and the slot counts.</summary>
        private static GladeStatus ComputeP(ComposeInput input, ComposeResult output, float3[] occupants, int m, GladePlanCache cache)
        {
            output.RequestedSlice = m;
            GladeStatus ps = BuildP(input, output, occupants, output.Layer, m, cache);
            if (ps != GladeStatus.Ok && ps != GladeStatus.Clamped)
                return ps;
            output.Slice = output.Plan.Planted.Count;
            output.RawTarget = output.Layer == LayerGlade ? output.Plan.RawTargetGlade : output.Plan.RawTargetBelt;

            float spacing = SafeNonNeg(input.Params.MinSpacing);
            MatchSlots(input.Own, output, spacing * 0.5);

            HashSet<int> skip = input.SkipSlots;
            int slice = output.Slice, skipped = 0, matched = 0, missing = 0;
            for (int s = 0; s < slice; s++)
            {
                bool isSkip = skip != null && skip.Contains(s);
                if (isSkip)
                    skipped++;
                if (output.SlotMatched[s])
                    matched++;
                else if (!isSkip)
                    missing++;
            }
            output.Skipped = skipped;
            output.Matched = matched;
            output.Missing = missing;
            int unmatched = 0;
            for (int k = 0; k < output.SameScratch.Count; k++)
            {
                if (output.MemberSlot[k] < 0)
                    unmatched++;
            }
            output.OwnUnmatched = unmatched;
            return ps;
        }

        private static GladeStatus BuildP(ComposeInput input, ComposeResult output, float3[] occupants, byte layer,
                                          int m, GladePlanCache cache)
        {
            GladePlan plan = output.Plan;
            output.CacheHit = false;
            output.FullReplan = false;
            if (cache != null)
            {
                GladePlan hit;
                bool hitIsSlice;
                if (cache.TryGet(output.PlanKey, m, out hit, out hitIsSlice))
                {
                    int n = hit.Planted.Count;
                    CopyPlan(hit, plan, m);
                    output.CacheHit = true;
                    output.PlanIsSlice = hitIsSlice || n > m;
                    if (n > m)
                        MarkSlice(plan, layer, m);
                    return plan.Status;
                }
            }

            GladeParams pp = StreamParams(input, layer);
            pp.MaxPlant = m;
            uint seed = SkiGladePlanner.Seed32(input.Ring[0], VariantSalt(input.Variant));
            GladeStatus st = SkiGladePlanner.Plan(input.Ring, input.Triangles, seed, in pp, input.Obstacles,
                occupants, null, input.ExclusionRings, plan);
            if (st != GladeStatus.Ok && st != GladeStatus.Clamped)
                return st;

            if (plan.Clamped && plan.Saturated)
            {
                // Engine notes, SLICE RAN OUT OF ATTEMPTS: re-plan in full (same stream, larger budget)
                // and cut back to m.
                pp.MaxPlant = int.MaxValue;
                st = SkiGladePlanner.Plan(input.Ring, input.Triangles, seed, in pp, input.Obstacles,
                    occupants, null, input.ExclusionRings, plan);
                if (st != GladeStatus.Ok && st != GladeStatus.Clamped)
                    return st;
                output.FullReplan = true;
            }

            bool fullIsSlice = plan.Clamped; // a budget still bound the (re-)plan
            if (cache != null)
                cache.Put(output.PlanKey, plan, fullIsSlice);
            int count = plan.Planted.Count;
            output.PlanIsSlice = fullIsSlice || count > m;
            if (count > m)
                MarkSlice(plan, layer, m);
            return plan.Status;
        }

        /// <summary>Cuts P to its first m slots and describes it as a clamped slice.</summary>
        private static void MarkSlice(GladePlan plan, byte layer, int m)
        {
            if (plan.Planted.Count > m)
                plan.Planted.RemoveRange(m, plan.Planted.Count - m);
            plan.Clamped = true;
            plan.Status = GladeStatus.Clamped;
            plan.Saturated = false;
            if (layer == LayerGlade)
                plan.TargetGlade = m;
            else
                plan.TargetBelt = m;
        }

        /// <summary>Copies src into dst, keeping at most maxPlanted trees (order preserved).</summary>
        internal static void CopyPlan(GladePlan src, GladePlan dst, int maxPlanted)
        {
            dst.Clear();
            int n = math.min(src.Planted.Count, maxPlanted);
            for (int i = 0; i < n; i++)
                dst.Planted.Add(src.Planted[i]);
            for (int i = 0; i < src.Thinned.Count; i++)
                dst.Thinned.Add(src.Thinned[i]);
            dst.Status = src.Status;
            dst.Area = src.Area;
            dst.BeltArea = src.BeltArea;
            dst.TargetGlade = src.TargetGlade;
            dst.TargetBelt = src.TargetBelt;
            dst.Attempts = src.Attempts;
            dst.Saturated = src.Saturated;
            dst.RawTargetGlade = src.RawTargetGlade;
            dst.RawTargetBelt = src.RawTargetBelt;
            dst.Clamped = src.Clamped;
        }

        /// <summary>SlotMatched[s] = some current member lies at xz distance &lt; radius from
        /// slot s; MemberSlot[k] = the slot current member k (SameScratch[k]) matches, or -1.
        /// Bucket grid over the members, cell >= radius, 3x3 query.</summary>
        private static void MatchSlots(ComposeMember[] own, ComposeResult output, double radius)
        {
            int slice = output.Plan.Planted.Count;
            if (output.SlotMatched.Length < slice)
                output.SlotMatched = new bool[math.max(slice, output.SlotMatched.Length * 2)];
            for (int s = 0; s < slice; s++)
                output.SlotMatched[s] = false;
            List<int> same = output.SameScratch;
            if (output.MemberSlot.Length < same.Count)
                output.MemberSlot = new int[math.max(same.Count, output.MemberSlot.Length * 2)];
            for (int k = 0; k < same.Count; k++)
                output.MemberSlot[k] = -1;
            if (!(radius > 0.0) || same.Count == 0 || slice == 0)
                return;

            double cell = math.max(radius, 0.25);
            double r2 = radius * radius;
            Dictionary<long, int> head = output.GridHead;
            head.Clear();
            if (output.GridNext.Length < same.Count)
                output.GridNext = new int[math.max(same.Count, output.GridNext.Length * 2)];
            int[] next = output.GridNext;
            for (int k = 0; k < same.Count; k++)
            {
                float3 pos = own[same[k]].Position;
                next[k] = -1;
                if (!math.isfinite(pos.x) || !math.isfinite(pos.z))
                    continue;
                long key = CellKey(math.floor(pos.x / cell), math.floor(pos.z / cell));
                int h;
                if (head.TryGetValue(key, out h))
                    next[k] = h;
                head[key] = k;
            }
            for (int s = 0; s < slice; s++)
            {
                float3 sp = output.Plan.Planted[s].Position;
                double cx = math.floor(sp.x / cell), cz = math.floor(sp.z / cell);
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        int k;
                        if (!head.TryGetValue(CellKey(cx + dx, cz + dz), out k))
                            continue;
                        while (k >= 0)
                        {
                            float3 mp = own[same[k]].Position;
                            double ddx = (double)mp.x - sp.x, ddz = (double)mp.z - sp.z;
                            if (ddx * ddx + ddz * ddz < r2)
                            {
                                output.SlotMatched[s] = true;
                                if (output.MemberSlot[k] < 0)
                                    output.MemberSlot[k] = s; // unique: slots are >= 2·radius apart
                            }
                            k = next[k];
                        }
                    }
                }
            }
            head.Clear();
        }

        private static long CellKey(double cx, double cz)
        {
            unchecked
            {
                long x = (long)math.clamp(cx, -1.0e9, 1.0e9);
                long z = (long)math.clamp(cz, -1.0e9, 1.0e9);
                return (x << 32) ^ (z & 0xFFFFFFFFL);
            }
        }

        private struct OrderKey
        {
            public int Group;     // 0 = matched (Key = slot), 1 = unmatched (Key = ThinHash)
            public uint Key;
            public float X, Z;
            public int Index;
        }

        private static readonly Comparison<OrderKey> s_KeepOrder = CompareKeep;
        private static readonly Comparison<OrderKey> s_XzOrder = CompareXz;
        private static readonly Comparison<OrderKey> s_RankOrder = CompareRank;

        private static int CompareKeep(OrderKey a, OrderKey b)
        {
            int c = a.Group.CompareTo(b.Group);
            if (c != 0)
                return c;
            c = a.Key.CompareTo(b.Key);
            if (c != 0)
                return c;
            return CompareXz(a, b);
        }

        private static int CompareXz(OrderKey a, OrderKey b)
        {
            int c = a.X.CompareTo(b.X);
            if (c != 0)
                return c;
            c = a.Z.CompareTo(b.Z);
            if (c != 0)
                return c;
            return a.Index.CompareTo(b.Index);
        }

        /// <summary>Fell rank: largest ThinHash first, then x, z, index ascending.</summary>
        private static int CompareRank(OrderKey a, OrderKey b)
        {
            int c = b.Key.CompareTo(a.Key);
            if (c != 0)
                return c;
            return CompareXz(a, b);
        }

        /// <summary>Other members of the layer, ascending (x, z, index), the first capRemove.</summary>
        private static int BuildRemoveOthers(ComposeMember[] own, ComposeResult output, int capRemove)
        {
            List<int> other = output.OtherScratch;
            if (other.Count == 0 || capRemove <= 0)
                return 0;
            var keys = new OrderKey[other.Count];
            for (int k = 0; k < other.Count; k++)
            {
                float3 pos = own[other[k]].Position;
                keys[k] = new OrderKey { X = pos.x, Z = pos.z, Index = other[k] };
            }
            Array.Sort(keys, s_XzOrder);
            int take = math.min(keys.Length, capRemove);
            for (int k = 0; k < take; k++)
                output.Remove.Add(keys[k].Index);
            return take;
        }

        /// <summary>In-region current members (SameScratch holds no other) in KEEP ORDER; appends
        /// those after the first `keep` to Remove, highest first, at most `budget`. Returns how
        /// many were appended.</summary>
        private static int BuildRemoveCountRule(ComposeMember[] own, ComposeResult output, int keep, int budget)
        {
            List<int> same = output.SameScratch;
            int nIn = same.Count;
            if (nIn <= keep || budget <= 0)
                return 0;
            var keys = new OrderKey[nIn];
            int w = 0;
            for (int k = 0; k < same.Count; k++)
            {
                float3 pos = own[same[k]].Position;
                int slot = output.MemberSlot[k];
                keys[w++] = new OrderKey
                {
                    Group = slot >= 0 ? 0 : 1,
                    Key = slot >= 0 ? (uint)slot : ThinHash(pos, ThinSeed),
                    X = pos.x,
                    Z = pos.z,
                    Index = same[k],
                };
            }
            Array.Sort(keys, s_KeepOrder);
            int take = math.min(nIn - keep, budget);
            for (int i = 0; i < take; i++)
                output.Remove.Add(keys[nIn - 1 - i].Index);
            return take;
        }

        /// <summary>Fell = the first `take` wild trees inside by RANK, emitted ascending.</summary>
        private static void BuildFell(ComposeInput input, ComposeResult output, int take)
        {
            List<int> wild = output.WildScratch;
            if (take <= 0 || wild.Count == 0)
                return;
            float3[] ex = input.Existing;
            var keys = new OrderKey[wild.Count];
            for (int k = 0; k < wild.Count; k++)
            {
                float3 pos = ex[wild[k]];
                keys[k] = new OrderKey { Key = ThinHash(pos, ThinSeed), X = pos.x, Z = pos.z, Index = wild[k] };
            }
            Array.Sort(keys, s_RankOrder);
            int n = math.min(take, keys.Length);
            var picked = new int[n];
            for (int k = 0; k < n; k++)
                picked[k] = keys[k].Index;
            Array.Sort(picked);
            for (int k = 0; k < n; k++)
                output.Fell.Add(picked[k]);
        }

        /// <summary>Plant = the first `want` unmatched, unskipped slots of P.</summary>
        private static void BuildPlant(ComposeInput input, ComposeResult output, int want)
        {
            HashSet<int> skip = input.SkipSlots;
            int slice = output.Slice;
            for (int s = 0; s < slice && output.Plant.Count < want; s++)
            {
                if (output.SlotMatched[s])
                    continue;
                if (skip != null && skip.Contains(s))
                    continue;
                output.Plant.Add(s);
            }
        }

        private static ulong XzBits(float3 p)
        {
            return ((ulong)math.asuint(p.x) << 32) | math.asuint(p.z);
        }

        private static ulong HashKey(ComposeInput input, byte layer, List<ulong> keyedOccupants)
        {
            GladeParams p = StreamParams(input, layer);
            ulong h = FnvOffset64;
            h = U32(h, 0x334B4C47u); // "GLK3": key format version
            h = Byte(h, layer);
            h = Byte(h, input.Variant);
            h = Byte(h, PlanGeneration);
            h = U32(h, (uint)p.Mode);
            h = F(h, p.ThinFraction);
            h = F(h, p.GladeDensity);
            h = F(h, p.BeltDensity);
            h = F(h, p.MinSpacing);
            h = F(h, p.InnerMargin);
            h = F(h, p.BeltGap);
            h = F(h, p.BeltDepth);
            h = F(h, p.BeltCoverage);
            h = F(h, p.BeltNoiseWavelength);
            h = U32(h, (uint)p.SpeciesCount);
            h = F(h, p.DominantShare);
            h = F(h, p.AgeWeights.x);
            h = F(h, p.AgeWeights.y);
            h = F(h, p.AgeWeights.z);
            h = F(h, p.AgeWeights.w);
            h = U32(h, (uint)p.AttemptsPerTree);

            float3[] ring = input.Ring;
            h = U32(h, ring != null ? (uint)ring.Length : 0xFFFFFFFFu);
            if (ring != null)
            {
                for (int i = 0; i < ring.Length; i++)
                {
                    h = F(h, ring[i].x);
                    h = F(h, ring[i].y);
                    h = F(h, ring[i].z);
                }
            }
            int3[] tris = input.Triangles;
            h = U32(h, tris != null ? (uint)tris.Length : 0xFFFFFFFFu);
            if (tris != null)
            {
                for (int i = 0; i < tris.Length; i++)
                {
                    h = U32(h, (uint)tris[i].x);
                    h = U32(h, (uint)tris[i].y);
                    h = U32(h, (uint)tris[i].z);
                }
            }
            GladeObstacle[] obs = input.Obstacles;
            h = U32(h, obs != null ? (uint)obs.Length : 0xFFFFFFFFu);
            if (obs != null)
            {
                for (int i = 0; i < obs.Length; i++)
                {
                    h = F(h, obs[i].Center.x);
                    h = F(h, obs[i].Center.z);
                    h = F(h, obs[i].Radius);
                }
            }
            float3[][] excl = input.ExclusionRings;
            h = U32(h, excl != null ? (uint)excl.Length : 0xFFFFFFFFu);
            if (excl != null && excl.Length > 0)
            {
                // Order-independent: the planner's ExclusionSet is a union.
                ulong[] ringHashes = new ulong[excl.Length];
                for (int r = 0; r < excl.Length; r++)
                {
                    float3[] er = excl[r];
                    ulong rh = FnvOffset64;
                    rh = U32(rh, er != null ? (uint)er.Length : 0xFFFFFFFFu);
                    if (er != null)
                    {
                        for (int i = 0; i < er.Length; i++)
                        {
                            rh = F(rh, er[i].x);
                            rh = F(rh, er[i].z);
                        }
                    }
                    ringHashes[r] = rh;
                }
                Array.Sort(ringHashes);
                for (int r = 0; r < ringHashes.Length; r++)
                    h = U64(h, ringHashes[r]);
            }

            // Occupants: order-independent (the planner's grid and its inside count are).
            ulong[] occ = keyedOccupants.ToArray();
            Array.Sort(occ);
            h = U32(h, (uint)occ.Length);
            for (int i = 0; i < occ.Length; i++)
                h = U64(h, occ[i]);
            return h != 0UL ? h : 1UL;
        }

        private static ulong Byte(ulong h, byte b)
        {
            unchecked
            {
                return (h ^ b) * FnvPrime64;
            }
        }

        private static ulong U32(ulong h, uint w)
        {
            h = Byte(h, (byte)w);
            h = Byte(h, (byte)(w >> 8));
            h = Byte(h, (byte)(w >> 16));
            return Byte(h, (byte)(w >> 24));
        }

        private static ulong U64(ulong h, ulong w)
        {
            h = U32(h, (uint)w);
            return U32(h, (uint)(w >> 32));
        }

        private static ulong F(ulong h, float v)
        {
            return U32(h, math.asuint(v));
        }

        private static float SafeNonNeg(float v)
        {
            if (!(v > 0f))
                return 0f;
            return v < 1.0e7f ? v : 1.0e7f; // as the planner's Knobs.NonNeg
        }

        private static int SatInt(long v)
        {
            return v > int.MaxValue ? int.MaxValue : v < 0 ? 0 : (int)v;
        }

        /// <summary>floor(v + 0.5) floored at 0 and capped at int.MaxValue (NaN → 0) — the
        /// planner's rounding of its raw targets.</summary>
        private static int RoundHalfUp(double v)
        {
            double r = math.floor(v + 0.5);
            if (!(r > 0.0))
                return 0;
            return r >= int.MaxValue ? int.MaxValue : (int)r;
        }

        /// <summary>The raw ring on xz in double, for boundary distances (inside-ness comes from
        /// the probe). Consecutive / closing duplicates are harmless: a zero-length edge's
        /// distance equals a neighboring edge's endpoint distance.</summary>
        private struct RingGeo
        {
            public double2[] V;
            public bool Ok;

            public static RingGeo From(float3[] ring)
            {
                RingGeo g = default;
                if (ring == null || ring.Length < 3)
                    return g;
                g.V = new double2[ring.Length];
                for (int i = 0; i < ring.Length; i++)
                {
                    if (!math.isfinite(ring[i].x) || !math.isfinite(ring[i].z))
                        return default;
                    g.V[i] = new double2(ring[i].x, ring[i].z);
                }
                g.Ok = true;
                return g;
            }

            public double BoundaryDistanceSq(double2 p)
            {
                double best = double.MaxValue;
                int n = V.Length;
                for (int i = 0; i < n; i++)
                {
                    double2 a = V[i];
                    double2 ab = V[(i + 1) % n] - a;
                    double2 ap = p - a;
                    double len2 = ab.x * ab.x + ab.y * ab.y;
                    double t = len2 > 0.0 ? (ap.x * ab.x + ap.y * ab.y) / len2 : 0.0;
                    if (t < 0.0)
                        t = 0.0;
                    else if (t > 1.0)
                        t = 1.0;
                    double2 d = ap - t * ab;
                    double dsq = d.x * d.x + d.y * d.y;
                    if (dsq < best)
                        best = dsq;
                }
                return best;
            }
        }
    }
}
