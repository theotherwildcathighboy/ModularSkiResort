// LodgeSupplyRule - the pure rules of the road-optional lodges (A-frame, Chalet, Grand: LodgeRoadRule.RoadOptional).
// At a road a lodge takes vanilla's services. Off-road, a cat track within LinkMeters whose chain reaches a road lodge or a bus depot
// supplies it: city utilities off, no warnings, no fee (LodgeSupplySystem). With neither, vanilla's warnings stand. The chain walks
// LiftServedRule.RunLinks over tracks only, inside one ski area. A lost link is held for HoldFrames so a section redo does not flip it.
// Guests still arrive only through a road lodge or the depot: an off-road lodge is never a ski-day target and has no parking row.
// Pure file: linked into tests/BuildingBatch (LodgeSupplyTests). Engine notes: EngineNotes/LodgeSupplySystem.md

using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>One lodge's published supply with its hold. A plain value the caller keeps per lodge.</summary>
    public struct LodgeSupplyState
    {
        public bool Known;        // seen at least once since the load
        public bool Supplied;     // the published answer
        public bool Losing;       // the raw answer has read false since LostSince, and Supplied still holds
        public uint LostSince;    // the sim frame of the first false read of this streak
    }

    /// <summary>What the switch does with one road-optional lodge.</summary>
    public enum LodgeSupplyMode : byte
    {
        /// <summary>A road edge: vanilla's consumers, plus mail and crime (clearing RequireRoad dropped them).</summary>
        Road = 0,
        /// <summary>No road edge, no chain: vanilla's consumers and warnings.</summary>
        Unsupplied = 1,
        /// <summary>No road edge, a chain: the five consumers off, icons and factors cleaned.</summary>
        OffGrid = 2,
    }

    public static class LodgeSupplyRule
    {
        /// <summary>One game hour in sim frames (= LiftClosedRule.HoldFrames). Our number, not vanilla's: a flip costs a re-add, a hold
        /// costs nothing, and it has to outlast deleting and redrawing a track section at any game speed.</summary>
        public const uint HoldFrames = 10923u;

        /// <summary>A consumer the switch tagged Updated for and that is still missing is tagged again after this many updates of the
        /// switch. So a prefab whose archetype lacks one (InitializeSystem.cs:178-208) is not tagged every frame.</summary>
        public const int RetagUpdates = 256;

        /// <summary>Rows LodgeRoadSystem W4 writes: every row but Base (which keeps the game's own road rule).</summary>
        public static bool WritesRoadFlags(LodgeRoadRule rule)
        {
            return rule != LodgeRoadRule.Base;
        }

        /// <summary>The W4 write on BuildingData.m_Flags (as uint, so the test host needs no game type).</summary>
        public static uint ClearRoadFlags(uint flags, uint requireAccess, uint requireRoad)
        {
            return flags & ~(requireAccess | requireRoad);
        }

        /// <summary>The Editor's way back: the game's value, only while the prefab still holds exactly what W4 wrote.</summary>
        public static bool TryRestoreRoadFlags(uint current, uint written, uint gameFlags, out uint restored)
        {
            restored = current == written ? gameFlags : current;
            return current == written;
        }

        /// <summary>A lodge row with a road edge that takes arrivals: a chain root, a ski-day target, a parking row. The Hut and Summit are
        /// not, even at a road.</summary>
        public static bool IsRoadLodge(LodgeRoadRule rule, bool hasRoad)
        {
            return hasRoad && (rule == LodgeRoadRule.Base || rule == LodgeRoadRule.RoadOptional);
        }

        /// <summary>Passes the ski-day road check without a road, and is never a claim target (SkiDayRules.LodgeFacts.NoRoadNeeded). At a
        /// road a road-optional lodge is a target. Off-road and unsupplied, it is rejected as before.</summary>
        public static bool NoRoadNeeded(LodgeRoadRule rule, bool hasRoad, bool supplied)
        {
            switch (rule)
            {
                case LodgeRoadRule.Base: return false;
                case LodgeRoadRule.RoadOptional: return !hasRoad && supplied;
                default: return true;
            }
        }

        /// <summary>Counts toward the hub's "not at a road" row.</summary>
        public static bool CountsAsNoRoad(LodgeRoadRule rule, bool hasRoad, bool supplied)
        {
            return !hasRoad && !NoRoadNeeded(rule, hasRoad, supplied);
        }

        /// <summary>The lodge's own jobs ride on the carrier (MountainJobsRules): it has no way for staff to arrive.</summary>
        public static bool JobsLeave(LodgeRoadRule rule, bool hasRoad, bool supplied)
        {
            return rule == LodgeRoadRule.RoadOptional && !hasRoad && supplied;
        }

        public static LodgeSupplyMode ModeOf(bool hasRoad, bool supplied)
        {
            return hasRoad ? LodgeSupplyMode.Road : supplied ? LodgeSupplyMode.OffGrid : LodgeSupplyMode.Unsupplied;
        }

        /// <summary>Tag Updated now? Road and Unsupplied want a missing consumer back through vanilla's path, at most once per RetagUpdates.
        /// <paramref name="updatesSinceTag"/> &lt; 0: not tagged in this streak.</summary>
        public static bool ShouldTagUpdated(LodgeSupplyMode mode, bool consumerMissing, bool destroyedOrAbandoned, int updatesSinceTag)
        {
            if (mode == LodgeSupplyMode.OffGrid || !consumerMissing || destroyedOrAbandoned)
                return false;
            return updatesSinceTag < 0 || updatesSinceTag >= RetagUpdates;
        }

        /// <summary>Mail and crime: added one by one, at a road only (a relocate back onto a road re-adds power, water and garbage, not these).</summary>
        public static bool AddsMailAndCrime(LodgeSupplyMode mode)
        {
            return mode == LodgeSupplyMode.Road;
        }

        /// <summary>One read of the raw answer. A true read publishes at once. A false read keeps Supplied until it has read false for
        /// HoldFrames (unchecked subtraction: the frame counter may wrap). Frames stop while paused, so a pause never runs the hold out.
        /// The first read starts from <paramref name="savedSupplied"/>, the shape the save holds, so a load flips nothing by
        /// itself.</summary>
        public static bool Step(ref LodgeSupplyState s, bool raw, uint nowFrame, bool savedSupplied)
        {
            if (!s.Known)
                Seed(ref s, savedSupplied);
            if (raw)
            {
                s.Supplied = true;
                s.Losing = false;
                return true;
            }
            if (!s.Supplied)
            {
                s.Losing = false;
                return false;
            }
            if (!s.Losing)
            {
                s.Losing = true;
                s.LostSince = nowFrame;
            }
            if (unchecked(nowFrame - s.LostSince) >= HoldFrames)
            {
                s.Supplied = false;
                s.Losing = false;
            }
            return s.Supplied;
        }

        public static void Seed(ref LodgeSupplyState s, bool savedSupplied)
        {
            s.Known = true;
            s.Supplied = savedSupplied;
            s.Losing = false;
        }

        /// <summary>A lodge at a road reads not supplied, with no hold: a road edge lost later starts from the raw answer alone. So the switch
        /// never strips a lodge on an answer computed while it was its own root.</summary>
        public static void AtRoad(ref LodgeSupplyState s)
        {
            s.Known = true;
            s.Supplied = false;
            s.Losing = false;
        }

        /// <summary>Marks every track a root reaches: a track within LinkMeters of a root of its area (LiftServedRule.OutlinesLinked), then along
        /// the links over tracks only. Allocation-free once <paramref name="reached"/> and <paramref name="stack"/> have grown. Links never
        /// cross areas (RunLinks.Build), so the walk stays in the ski area it started in.</summary>
        public static void Reach(IReadOnlyList<float2[]> outlines, IReadOnlyList<int> areas, IReadOnlyList<bool> isTrack, LiftServedRule.RunLinks links,
                                 IReadOnlyList<float2[]> roots, IReadOnlyList<int> rootAreas, List<bool> reached, List<int> stack)
        {
            reached.Clear();
            stack.Clear();
            int n = outlines.Count;
            if (areas.Count < n) n = areas.Count;
            if (isTrack.Count < n) n = isTrack.Count;
            if (links.Count < n) n = links.Count;
            for (int i = 0; i < n; i++)
                reached.Add(false);
            for (int r = 0; r < roots.Count && r < rootAreas.Count; r++)
            {
                for (int i = 0; i < n; i++)
                {
                    if (reached[i] || !isTrack[i] || areas[i] != rootAreas[r] || !LiftServedRule.OutlinesLinked(roots[r], outlines[i]))
                        continue;
                    reached[i] = true;
                    stack.Add(i);
                }
            }
            while (stack.Count > 0)
            {
                int run = stack[stack.Count - 1];
                stack.RemoveAt(stack.Count - 1);
                links.NeighboursOf(run, out int first, out int count);
                for (int k = 0; k < count; k++)
                {
                    int m = links.Neighbour(first + k);
                    if (m >= n || reached[m] || !isTrack[m])
                        continue;
                    reached[m] = true;
                    stack.Add(m);
                }
            }
        }

        /// <summary>The raw answer for one off-road lodge: a reached track of its area within LinkMeters of its lot.</summary>
        public static bool Supplied(float2[] lodge, int lodgeArea, IReadOnlyList<float2[]> outlines, IReadOnlyList<int> areas, IReadOnlyList<bool> reached)
        {
            for (int i = 0; i < reached.Count; i++)
            {
                if (reached[i] && areas[i] == lodgeArea && LiftServedRule.OutlinesLinked(lodge, outlines[i]))
                    return true;
            }
            return false;
        }
    }
}
