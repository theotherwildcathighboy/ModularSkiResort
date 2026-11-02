// SkiResortAttractivenessSystem, merged buildings (MergeRules header; SkiMergedLodge). A merged building is a top-level lodge of the
// tier its tag names (LodgeIdentity), linked to a sign like our own lodges, and counted only within SkiDayRules.SlopesideMaxM of a run or
// a lift station of that area, re-checked every recompute. It roots no supply chain, is never the job carrier and needs no road: its
// tier's jobs ride on the carrier like a mountain lodge's. Its AttractivenessProvider is never removed (vanilla cannot add a prefab's own
// back): the write combines its own vanilla value V with the share P (WriteProvidersJob, Merged).
// Engine notes: EngineNotes/SkiMergeSystem.md

using System.Collections.Generic;
using Colossal.Entities;
using Game.Buildings;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiResortAttractivenessSystem
    {
        /// <summary>A merged building linked to an area, waiting for the area's runs and lift stations (PlaceMerged).</summary>
        private struct MergedCandidate
        {
            public Entity Lodge;
            public LodgeDef Def;
            public bool Active;
            public bool HasRoad;
            public bool Destroyed;
            public float Own;   // its own efficiency (1 without factors)
        }

        /// <summary>One merged building as the last recompute saw it: the hub row and the card read this.</summary>
        internal struct MergedView
        {
            public Entity Building;
            public Entity Sign;          // Entity.Null = no area
            public int Tier;             // SkiCatalog.Lodges index
            public MergeRules.State State;
        }

        // This recompute's merged buildings: tier by entity (the walk), and the published views (swapped at the end of PlaceMerged).
        private readonly Dictionary<Entity, int> m_MergedTier = new Dictionary<Entity, int>();
        private Dictionary<Entity, MergedView> m_MergedViews = new Dictionary<Entity, MergedView>();
        private Dictionary<Entity, MergedView> m_MergedViewsNext = new Dictionary<Entity, MergedView>();
        private int m_MergedRevision;
        private bool m_SimTick;   // WriteProviders runs from OnUpdate, right after vanilla's write of this frame (not a paused recompute)

        /// <summary>++ when the published merged views change (the hub re-copies).</summary>
        internal int MergedRevision => m_MergedRevision;

        /// <summary>The merged buildings of the last recompute, any state; clears <paramref name="into"/>.</summary>
        internal int CopyMergedViews(List<MergedView> into)
        {
            into.Clear();
            foreach (var pair in m_MergedViews)
                into.Add(pair.Value);
            return into.Count;
        }

        internal bool TryGetMergedView(Entity building, out MergedView view)
        {
            return m_MergedViews.TryGetValue(building, out view);
        }

        /// <summary>SkiMergeSystem after a merge, an unmerge or a tier change: recompute on the next tick (or now while paused).</summary>
        internal void RequestMergedRecompute()
        {
            RequestRecompute(ReasonMerged);
            if (m_Simulation != null && m_Simulation.selectedSpeed == 0f)
                RecomputeWhilePaused();
        }

        internal const string ReasonMerged = "merged building changed";

        private void ResetMerged()
        {
            m_MergedTier.Clear();
            m_MergedViews.Clear();
            m_MergedViewsNext.Clear();
            m_MergedRevision++;
        }

        /// <summary>The merged walk, after the park walk: every top-level tagged building that is not ours and still qualifies (Classify)
        /// joins m_LiveLodges and the top-level list as a lodge. One that no longer qualifies (a changed or missing asset) is listed as
        /// AssetMissing and counted for nothing. The tag is never removed here.</summary>
        private void CollectMerged()
        {
            m_MergedTier.Clear();
            m_MergedViewsNext.Clear();
            if (m_MergedQuery.IsEmptyIgnoreFilter)
                return;
            HashSet<Entity> lodgePrefabs = m_PrefabCatalog.LodgePrefabSet;
            using (var entities = m_MergedQuery.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity building = entities[i];
                    if (!EntityManager.TryGetComponent(building, out PrefabRef prefabRef) || lodgePrefabs.Contains(prefabRef.m_Prefab))
                        continue;   // our own lodge: its catalog row wins (LodgeIdentity), the park walk has it
                    SkiMergedLodge tag = EntityManager.GetComponentData<SkiMergedLodge>(building);
                    var view = new MergedView { Building = building, Sign = Entity.Null, Tier = tag.m_Tier, State = MergeRules.State.NoArea };
                    MergeRules.Facts facts = SkiMergeSystem.ReadFacts(EntityManager, m_PrefabCatalog, building, forTag: true);
                    bool kindOk = MergeRules.Classify(facts, out _) != MergeRules.Kind.None && tag.m_Tier < SkiCatalog.Lodges.Count;
                    if (!kindOk)
                    {
                        view.State = MergeRules.State.AssetMissing;
                        m_MergedViewsNext[building] = view;
                        continue;
                    }
                    m_MergedViewsNext[building] = view;
                    if (EntityManager.HasComponent<Owner>(building))
                        continue;
                    m_MergedTier[building] = tag.m_Tier;
                    m_LiveLodges.Add(building);
                    AddTopLevel(building, SchoolLinkRules.KindLodge);
                }
            }
        }

        /// <summary>The tier row of a merged building of this walk (LodgeIdentity.Resolve with no catalog row); null when not merged.</summary>
        private LodgeDef MergedDefOf(Entity building)
        {
            return m_MergedTier.TryGetValue(building, out int tier) ? LodgeIdentity.Resolve(null, true, tier, SkiCatalog.Lodges) : null;
        }

        /// <summary>After the runs and lifts of this recompute are known: each linked merged building within SlopesideMaxM of a run outline
        /// or a lift station of its own area joins the area as an active (or switched-off) lodge, its tier weight on the carrier's staffing
        /// like a mountain lodge's. Then the views are published.</summary>
        private void PlaceMerged(Snapshot next)
        {
            for (int s = 0; s < next.SlotCount; s++)
            {
                AreaSlot slot = next.Slots[s];
                for (int p = 0; p < slot.MergedPending.Count; p++)
                {
                    MergedCandidate c = slot.MergedPending[p];
                    MergeRules.State state;
                    if (c.Destroyed)
                        state = MergeRules.State.Destroyed;
                    else if (!Slopeside(c.Lodge, s, slot))
                        state = MergeRules.State.TooFar;
                    else if (!c.Active)
                    {
                        state = MergeRules.State.Inactive;
                        slot.Inactive.Add(c.Lodge);
                    }
                    else
                    {
                        state = MergeRules.State.Counted;
                        slot.Active.Add(c.Lodge);
                        slot.ActiveInfo.Add(new LodgeInfo
                        {
                            Lodge = c.Lodge,
                            LodgeIndex = (short)c.Def.Index,
                            SummitServed = false,
                            HasRoad = c.HasRoad,
                            Supplied = false,
                            Merged = true,
                        });
                        double weight = c.Def.B;
                        if (weight > 0.0)
                        {
                            slot.CarriedOwn.Add(c.Own);
                            slot.CarriedWeight.Add(weight);
                            slot.MountainInMean = true;
                        }
                    }
                    if (m_MergedViewsNext.TryGetValue(c.Lodge, out MergedView view))
                    {
                        view.Sign = slot.Sign;
                        view.State = state;
                        m_MergedViewsNext[c.Lodge] = view;
                    }
                }
            }
            if (!SameMergedViews(m_MergedViews, m_MergedViewsNext))
                m_MergedRevision++;
            Dictionary<Entity, MergedView> swap = m_MergedViews;
            m_MergedViews = m_MergedViewsNext;
            m_MergedViewsNext = swap;
        }

        /// <summary>Within SkiDayRules.SlopesideMaxM of a lift station or a run outline of area <paramref name="slotIndex"/>.</summary>
        private bool Slopeside(Entity building, int slotIndex, AreaSlot slot)
        {
            if (!TryCachedXz(m_TopXz, building, out float2 xz))
                return false;
            float max = SkiDayRules.SlopesideMaxM;
            for (int i = 0; i < slot.StationXz.Count; i++)
            {
                if (math.distancesq(xz, slot.StationXz[i]) <= max * max)
                    return true;
            }
            for (int r = 0; r < m_RunOutlines.Count && r < m_RunAreas.Count; r++)
            {
                if (m_RunAreas[r] == slotIndex && LiftServedRule.OutlineWithin(xz, m_RunOutlines[r], max))
                    return true;
            }
            return false;
        }

        private static bool SameMergedViews(Dictionary<Entity, MergedView> a, Dictionary<Entity, MergedView> b)
        {
            if (a.Count != b.Count)
                return false;
            foreach (var pair in b)
            {
                if (!a.TryGetValue(pair.Key, out MergedView x))
                    return false;
                MergedView y = pair.Value;
                if (x.Sign != y.Sign || x.Tier != y.Tier || x.State != y.State)
                    return false;
            }
            return true;
        }

        /// <summary>One merged building's write for WriteProvidersJob: the share P, flagged OwnSlice on the tick of its own update slice
        /// (MergeRules.IsOwnSlice; UpdateFrame is a shared component, a chunk read with no sync). Adds the runtime V cache when missing
        /// (it is never saved). False without a provider (SkiMergeSystem adds one at merge).</summary>
        private bool TryMergedWrite(Entity building, int share, out ProviderWrite write)
        {
            write = default;
            if (!EntityManager.HasComponent<AttractivenessProvider>(building))
                return false;
            if (!EntityManager.HasComponent<SkiMergedVanillaAttraction>(building))
                EntityManager.AddComponentData(building, new SkiMergedVanillaAttraction());
            // Only on the simulation tick: a paused recompute's write would read back our own last value as V and grow it.
            bool ownSlice = false;
            if (m_SimTick && EntityManager.HasComponent<UpdateFrame>(building))
            {
                uint slice = SimulationUtils.GetUpdateFrameWithInterval(m_Simulation.frameIndex, 16, 16);
                ownSlice = MergeRules.IsOwnSlice(slice, EntityManager.GetSharedComponent<UpdateFrame>(building).m_Index);
            }
            write = new ProviderWrite { Target = building, Value = share, Merged = true, OwnSlice = ownSlice };
            return true;
        }
    }
}
