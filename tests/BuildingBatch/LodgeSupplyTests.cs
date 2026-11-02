// LodgeSupplyRule and the job carrier. LS1-LS3 the catalog rows and the W4 round trip; LS4-LS9 the chain walk over real RunLinks; LS10 the
// hold; LS11-LS13 the switch's decisions and the per-building road test; LC1-LC6 the carrier rule (b1).

using System;
using System.Collections.Generic;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace BuildingBatch
{
    internal static class LodgeSupplyTests
    {
        private const int AreaA = 0, AreaB = 1;

        /// <summary>A 4 m wide east-west strip from x0 to x1 at depth z: a stand-in track outline.</summary>
        private static float2[] Strip(float x0, float x1, float z)
        {
            return new[] { new float2(x0, z - 2f), new float2(x1, z - 2f), new float2(x1, z + 2f), new float2(x0, z + 2f) };
        }

        /// <summary>A 16 x 16 m lot centered on (x, z): a stand-in lodge or depot ring.</summary>
        private static float2[] Lot(float x, float z)
        {
            return new[] { new float2(x - 8f, z - 8f), new float2(x + 8f, z - 8f), new float2(x + 8f, z + 8f), new float2(x - 8f, z + 8f) };
        }

        /// <summary>Tracks and other runs of one recompute, built as the attraction system does; one answer per lodge.</summary>
        private sealed class Scene
        {
            public readonly List<float2[]> Outlines = new List<float2[]>();
            public readonly List<int> Areas = new List<int>();
            public readonly List<bool> IsTrack = new List<bool>();
            public readonly List<float2[]> Roots = new List<float2[]>();
            public readonly List<int> RootAreas = new List<int>();
            private readonly LiftServedRule.RunLinks m_Links = new LiftServedRule.RunLinks();
            private readonly List<bool> m_Reached = new List<bool>();
            private readonly List<int> m_Stack = new List<int>();

            public Scene Track(float x0, float x1, float z, int area = AreaA)
            {
                Outlines.Add(Strip(x0, x1, z));
                Areas.Add(area);
                IsTrack.Add(true);
                return this;
            }

            public Scene Run(float x0, float x1, float z, int area = AreaA)
            {
                Outlines.Add(Strip(x0, x1, z));
                Areas.Add(area);
                IsTrack.Add(false);
                return this;
            }

            public Scene Root(float x, float z, int area = AreaA)
            {
                Roots.Add(Lot(x, z));
                RootAreas.Add(area);
                return this;
            }

            public bool Supplied(float x, float z, int area = AreaA)
            {
                m_Links.Build(Outlines, Areas);
                LodgeSupplyRule.Reach(Outlines, Areas, IsTrack, m_Links, Roots, RootAreas, m_Reached, m_Stack);
                return LodgeSupplyRule.Supplied(Lot(x, z), area, Outlines, Areas, m_Reached);
            }
        }

        public static void Run(Func<bool, string, string, bool> check)
        {
            // LS1: the rows. The three lodges with their own jobs are road-optional; the Hut and Summit keep needing neither.
            foreach (string id in new[] { "aFrame", "mid", "grand" })
            {
                LodgeDef def = Lodge(id);
                check(def.RoadRule == LodgeRoadRule.RoadOptional && def.Jobs > 0 && def.SignJobs == 0, "LS1", $"{id} is road-optional with its own jobs");
            }
            check(Lodge("hut").RoadRule == LodgeRoadRule.Mountain && Lodge("summit").RoadRule == LodgeRoadRule.Mountain, "LS1b", "Hut and Summit stay Mountain");
            check(SkiCatalog.SchoolByPrefab("SkiSchool").RoadRule == LodgeRoadRule.Base && SkiCatalog.Depots[0].RoadRule == LodgeRoadRule.Base, "LS1c",
                  "school and depot stay road buildings");
            check((byte)LodgeRoadRule.RoadOptional == 3 && (byte)LodgeRoadRule.None == 2, "LS1d", "the enum only appended");

            // LS2-LS3: W4 writes every road-optional row and hands back the game's value for the Editor.
            const uint requireRoad = 1u, requireAccess = 16u, other = 64u;
            check(LodgeSupplyRule.WritesRoadFlags(LodgeRoadRule.RoadOptional) && LodgeSupplyRule.WritesRoadFlags(LodgeRoadRule.Mountain) &&
                  LodgeSupplyRule.WritesRoadFlags(LodgeRoadRule.None) && !LodgeSupplyRule.WritesRoadFlags(LodgeRoadRule.Base), "LS2", "W4 skips only Base rows");
            uint game = requireRoad | requireAccess | other;
            uint written = LodgeSupplyRule.ClearRoadFlags(game, requireAccess, requireRoad);
            check(written == other, "LS2b", $"both flags go, the rest stays (0x{written:X})");
            bool back = LodgeSupplyRule.TryRestoreRoadFlags(written, written, game, out uint restored);
            check(back && restored == game, "LS3", "Editor load: the game's flags come back");
            back = LodgeSupplyRule.TryRestoreRoadFlags(written | 128u, written, game, out restored);
            check(!back && restored == (written | 128u), "LS3b", "the prefab changed since the write: nothing restored");

            // LS4: the plain chain. Road lodge at x 0; three tracks in a row; off-road lodge at the far end.
            Scene s = new Scene().Root(0f, 0f).Track(20f, 200f, 0f).Track(210f, 400f, 0f).Track(410f, 600f, 0f);
            check(s.Supplied(620f, 0f), "LS4", "three tracks in a row carry the chain to a lodge at the far end");
            check(!s.Supplied(1000f, 0f), "LS4b", "the same chain, a lodge 400 m beyond its end: no supply");

            // LS5: the depot root is the same root; the rule never asks which kind it is.
            s = new Scene().Root(0f, 500f).Track(20f, 200f, 500f);
            check(s.Supplied(220f, 500f), "LS5", "a depot (or road lodge) root supplies through one track");

            // LS6: no chain.
            s = new Scene().Track(20f, 200f, 0f);
            check(!s.Supplied(220f, 0f), "LS6", "a track with no root supplies nothing");
            s = new Scene().Root(0f, 0f);
            check(!s.Supplied(16f, 0f), "LS6b", "a road lodge next door with no track: no supply (the chain is the track)");

            // LS7: the 25 m contact, measured from the lot, not the center, on both ends.
            s = new Scene().Root(0f, 0f).Track(32f, 200f, 0f);   // root lot edge x 8, track starts 32: 24 m
            check(s.Supplied(233f, 0f), "LS7", "24 m at the root, 25 m at the lodge (lot edge 225): linked");
            check(!s.Supplied(234f, 0f), "LS7b", "26 m at the lodge: not linked");
            s = new Scene().Root(0f, 0f).Track(34f, 200f, 0f);   // 26 m from the root's lot
            check(!s.Supplied(210f, 0f), "LS7c", "26 m at the root: not linked");

            // LS8: a chain is over tracks only: a ski run between two tracks does not carry it.
            s = new Scene().Root(0f, 0f).Track(20f, 100f, 0f).Run(110f, 300f, 0f).Track(310f, 400f, 0f);
            check(!s.Supplied(420f, 0f), "LS8", "a run in the gap breaks the chain");
            check(s.Supplied(90f, 30f), "LS8b", "the first track still supplies a lodge beside it (20 m)");

            // LS9: links stay in one ski area, and a root of another area is no root here.
            s = new Scene().Root(0f, 0f, AreaB).Track(20f, 200f, 0f);
            check(!s.Supplied(220f, 0f), "LS9", "a root of another area does not reach this area's track");
            s = new Scene().Root(0f, 0f).Track(20f, 100f, 0f).Track(110f, 200f, 0f, AreaB);
            check(!s.Supplied(220f, 0f, AreaB), "LS9b", "the chain does not cross into another area");

            // LS10: the hold.
            var st = default(LodgeSupplyState);
            check(!LodgeSupplyRule.Step(ref st, false, 100u, false) && st.Known && !st.Losing, "LS10", "first read, saved on the grid, no chain: unsupplied");
            check(LodgeSupplyRule.Step(ref st, true, 200u, false), "LS10b", "a chain appears: supplied at once");
            check(LodgeSupplyRule.Step(ref st, false, 300u, false) && st.Losing, "LS10c", "the link goes (a section redo starts): still supplied");
            check(LodgeSupplyRule.Step(ref st, false, 300u + LodgeSupplyRule.HoldFrames - 1u, false), "LS10d", "held for HoldFrames - 1");
            check(LodgeSupplyRule.Step(ref st, true, 300u + LodgeSupplyRule.HoldFrames - 1u, false) && !st.Losing, "LS10e", "the redrawn section ends the streak");
            LodgeSupplyRule.Step(ref st, false, 20000u, false);
            check(LodgeSupplyRule.Step(ref st, false, 20000u + LodgeSupplyRule.HoldFrames - 1u, false), "LS10f", "a new streak starts its own clock");
            check(!LodgeSupplyRule.Step(ref st, false, 20000u + LodgeSupplyRule.HoldFrames, false) && !st.Losing, "LS10g", "a real removal lapses at HoldFrames");
            st = default;
            check(LodgeSupplyRule.Step(ref st, false, 500u, true) && st.Losing, "LS10h", "a save with the utilities off and no chain read yet does not flip at load");
            st = default;
            LodgeSupplyRule.Step(ref st, true, uint.MaxValue - 5u, false);
            LodgeSupplyRule.Step(ref st, false, uint.MaxValue - 5u, false);
            check(LodgeSupplyRule.Step(ref st, false, 100u, false), "LS10i", "the frame counter wraps inside the hold: still held");
            check(!LodgeSupplyRule.Step(ref st, false, LodgeSupplyRule.HoldFrames, false), "LS10j", "and lapses on time across the wrap");
            st = default;
            LodgeSupplyRule.Step(ref st, true, 10u, false);
            LodgeSupplyRule.AtRoad(ref st);
            check(!st.Supplied && !LodgeSupplyRule.Step(ref st, false, 11u, false), "LS10k", "at a road no hold survives: a road lost later reads the raw answer alone");

            // LS11: the three modes.
            check(LodgeSupplyRule.ModeOf(true, true) == LodgeSupplyMode.Road && LodgeSupplyRule.ModeOf(true, false) == LodgeSupplyMode.Road &&
                  LodgeSupplyRule.ModeOf(false, true) == LodgeSupplyMode.OffGrid && LodgeSupplyRule.ModeOf(false, false) == LodgeSupplyMode.Unsupplied, "LS11",
                  "a road wins; no road: the chain decides");

            // LS12: the Updated tag: limit 5 (destroyed, abandoned), never at OffGrid, never every frame.
            check(LodgeSupplyRule.ShouldTagUpdated(LodgeSupplyMode.Road, true, false, -1) && LodgeSupplyRule.ShouldTagUpdated(LodgeSupplyMode.Unsupplied, true, false, -1), "LS12",
                  "a missing consumer is tagged back on both grid modes");
            check(!LodgeSupplyRule.ShouldTagUpdated(LodgeSupplyMode.Road, false, false, -1) && !LodgeSupplyRule.ShouldTagUpdated(LodgeSupplyMode.OffGrid, true, false, -1) &&
                  !LodgeSupplyRule.ShouldTagUpdated(LodgeSupplyMode.Road, true, true, -1), "LS12b", "nothing missing, off-grid, or destroyed / abandoned: no tag");
            check(!LodgeSupplyRule.ShouldTagUpdated(LodgeSupplyMode.Road, true, false, 1) && LodgeSupplyRule.ShouldTagUpdated(LodgeSupplyMode.Road, true, false, LodgeSupplyRule.RetagUpdates), "LS12c",
                  "still missing after a tag (a prefab without that consumer): retried at intervals, not every frame");
            check(LodgeSupplyRule.AddsMailAndCrime(LodgeSupplyMode.Road) && !LodgeSupplyRule.AddsMailAndCrime(LodgeSupplyMode.Unsupplied) && !LodgeSupplyRule.AddsMailAndCrime(LodgeSupplyMode.OffGrid), "LS12d",
                  "mail and crime at a road only");

            // LS13: the per-building road test, row by row.
            var opt = LodgeRoadRule.RoadOptional;
            check(!LodgeSupplyRule.NoRoadNeeded(opt, true, false) && !LodgeSupplyRule.NoRoadNeeded(opt, true, true), "LS13", "at a road: needs it (a ski-day target)");
            check(LodgeSupplyRule.NoRoadNeeded(opt, false, true), "LS13b", "off-road on a chain: passes, never a target");
            check(!LodgeSupplyRule.NoRoadNeeded(opt, false, false), "LS13c", "off-road, no chain: rejected as before");
            check(LodgeSupplyRule.NoRoadNeeded(LodgeRoadRule.Mountain, false, false) && LodgeSupplyRule.NoRoadNeeded(LodgeRoadRule.Mountain, true, false) &&
                  !LodgeSupplyRule.NoRoadNeeded(LodgeRoadRule.Base, false, true), "LS13d", "Mountain never needs it, even at a road; Base always does");
            check(LodgeSupplyRule.IsRoadLodge(opt, true) && !LodgeSupplyRule.IsRoadLodge(opt, false) && !LodgeSupplyRule.IsRoadLodge(LodgeRoadRule.Mountain, true), "LS13e",
                  "parking and chain roots: a road lodge only, and a Hut at a road is none");
            check(LodgeSupplyRule.CountsAsNoRoad(opt, false, false) && !LodgeSupplyRule.CountsAsNoRoad(opt, false, true) && !LodgeSupplyRule.CountsAsNoRoad(opt, true, false) &&
                  !LodgeSupplyRule.CountsAsNoRoad(LodgeRoadRule.Mountain, false, false), "LS13f", "the hub row: only off-road with no chain");
            check(LodgeSupplyRule.JobsLeave(opt, false, true) && !LodgeSupplyRule.JobsLeave(opt, true, true) && !LodgeSupplyRule.JobsLeave(opt, false, false) &&
                  !LodgeSupplyRule.JobsLeave(LodgeRoadRule.Mountain, false, true), "LS13g", "own jobs leave only an off-road lodge on a chain");

            // LC1: the pick: nearest to the sign, ties by entity.
            EntityRef a = new EntityRef(10, 1), b = new EntityRef(20, 1);
            check(MountainJobsRules.BetterCarrier(100f, a, float.PositiveInfinity, EntityRef.Null), "LC1", "the first candidate wins over none");
            check(MountainJobsRules.BetterCarrier(90f, b, 100f, a) && !MountainJobsRules.BetterCarrier(110f, b, 100f, a), "LC1b", "the nearer lodge wins");
            check(!MountainJobsRules.BetterCarrier(100f, b, 100f, a) && MountainJobsRules.BetterCarrier(100f, a, 100f, b), "LC1c", "a tie goes to the lower entity, so it is stable");

            // LC2: the one writer's sum.
            check(MountainJobsRules.CarrierTarget(12, 14) == 26, "LC2", "Chalet 12 + carried 14 = 26");
            check(MountainJobsRules.CarrierTarget(OffSeasonRules.Target(12, true, true), OffSeasonRules.HalfJobs(4) + OffSeasonRules.HalfJobs(8) + 2) == 6 + 2 + 4 + 2, "LC2b",
                  "off-season: own and lodge rows halved, patrol full");
            check(MountainJobsRules.CarrierTarget(-3, -1) == 0 && MountainJobsRules.CarrierTarget(0, 12) == 12, "LC2c", "a negative reads 0; a lodge with own 0 still carries");

            // LC3: an off-road lodge's jobs through the sign path: same row arithmetic.
            check(MountainJobsRules.SignJobsFor(Lodge("mid").Jobs, false, true, false) == 12 && MountainJobsRules.SignJobsFor(Lodge("mid").Jobs, true, true, false) == 0 &&
                  MountainJobsRules.SignJobsFor(Lodge("grand").Jobs, false, true, true) == 9, "LC3", "moved Chalet 12, Inactive 0, moved Grand off-season 9");

            // LC4: the totals. However the jobs are arranged, the area's workplaces do not change.
            int hut = Lodge("hut").SignJobs, summit = Lodge("summit").SignJobs, mid = Lodge("mid").Jobs, aFrame = Lodge("aFrame").Jobs, grand = Lodge("grand").Jobs;
            int signOnly = hut + summit;                                                  // sign carries, no lodge at a road
            int onLodge = MountainJobsRules.CarrierTarget(mid, hut + summit);             // Chalet at a road carries
            check(onLodge == signOnly + mid, "LC4", $"Hut + Summit move from the sign to a Chalet at a road: {onLodge} = {signOnly} + {mid}");
            int offRoad = MountainJobsRules.CarrierTarget(mid, grand + hut);              // Grand off-road on a chain, Chalet carries
            check(offRoad + 0 == mid + grand + hut, "LC4b", $"an off-road Grand's 18 move too: {offRoad}");
            check(MountainJobsRules.SignJobsFor(aFrame, false, true, false) == 4, "LC4c", "an off-road A-frame hands over its 4");

            // LC5: the off-road lodge's WorkProvider: gone only once the game's tick has laid the staff off, back when its jobs return.
            check(!MountainJobsRules.CanDropProvider(true, 12, 12), "LC5", "jobs just left: first to 0 workplaces");
            check(!MountainJobsRules.CanDropProvider(true, 0, 3), "LC5b", "0 workplaces, staff still listed: wait for the tick (no orphaned Worker rows)");
            check(MountainJobsRules.CanDropProvider(true, 0, 0), "LC5c", "0 workplaces, no staff: the component goes");
            check(!MountainJobsRules.CanDropProvider(false, 0, 0), "LC5d", "a lodge at a road off-season at 0 keeps it");

            // LC6: a depot-only chain has no lodge carrier, so the off-road lodge's jobs stay on the sign (the sum rule keys by sign).
            var rows = new Dictionary<EntityRef, int>();
            MountainJobsRules.SumBySign(new[] { new EntityRef(1, 1) }, new[] { mid }, new[] { true }, rows);
            check(rows[new EntityRef(1, 1)] == 12, "LC6", "no carrier lodge: an off-road Chalet's 12 on the sign");
        }

        private static LodgeDef Lodge(string id)
        {
            foreach (LodgeDef l in SkiCatalog.Lodges)
            {
                if (l.Id == id)
                    return l;
            }
            throw new InvalidOperationException("no lodge row " + id);
        }
    }
}
