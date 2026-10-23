// OffSeasonRules: half staffing while a building's lifts have no open run. OS1-OS3 the Step state machine (first
// observation, 12-hour hold to off, 1-hour hold back on, wrap-safe frame clock); OS4 HalfJobs / Target and the catalog rows;
// OS5 IsSeasonal; OS6 a 12-day calendar replay; OS7 sign jobs.

using System;
using SkiResortMod.Systems;

namespace BuildingBatch
{
    internal static class OffSeasonTests
    {
        private const uint Day = 262144u;

        public static void Run(Func<bool, string, string, bool> check)
        {
            var s = default(OffSeasonState);
            check(OffSeasonRules.Step(ref s, true, 1000u) && s.Known && !s.Pending, "OS1", "closed at the first observation -> off at once");
            s = default;
            check(!OffSeasonRules.Step(ref s, false, 1000u) && s.Known, "OS1b", "open at the first observation -> in season");

            s = default;
            OffSeasonRules.Step(ref s, false, 0u);
            bool offEarly = OffSeasonRules.Step(ref s, true, 100u) || OffSeasonRules.Step(ref s, true, 100u + OffSeasonRules.HoldToOffFrames - 1u);
            check(!offEarly && s.Pending, "OS2", "closed for HoldToOffFrames - 1 frames: still in season (pending)");
            check(OffSeasonRules.Step(ref s, true, 100u + OffSeasonRules.HoldToOffFrames) && !s.Pending, "OS2b",
                  "closed for exactly HoldToOffFrames (131,072 = 12 game hours): off-season");
            s = default;
            OffSeasonRules.Step(ref s, false, 0u);
            OffSeasonRules.Step(ref s, true, 10u);
            OffSeasonRules.Step(ref s, false, 50000u);                       // a run reopens: the clock resets
            bool afterFlicker = OffSeasonRules.Step(ref s, true, 60000u) || OffSeasonRules.Step(ref s, true, 10u + OffSeasonRules.HoldToOffFrames);
            check(!afterFlicker, "OS2c", "a thaw interrupted by an open run restarts the 12-hour clock");
            check(OffSeasonRules.HoldToOffFrames == Day / 2u && OffSeasonRules.HoldToOnFrames == 10923u, "OS2d",
                  "holds: 12 game hours to off, one game hour (LiftClosedRule.HoldFrames) back on");

            s = default;
            OffSeasonRules.Step(ref s, true, uint.MaxValue - 100u);          // off at load, near the wrap
            bool onEarly = !OffSeasonRules.Step(ref s, false, uint.MaxValue - 50u) ||
                           !OffSeasonRules.Step(ref s, false, unchecked(uint.MaxValue - 50u + OffSeasonRules.HoldToOnFrames - 1u));
            check(!onEarly, "OS3", "open for less than one game hour: still off-season (across the uint wrap)");
            check(!OffSeasonRules.Step(ref s, false, unchecked(uint.MaxValue - 50u + OffSeasonRules.HoldToOnFrames)), "OS3b",
                  "open for one game hour: in season (wrap-safe)");

            check(OffSeasonRules.HalfJobs(4) == 2 && OffSeasonRules.HalfJobs(12) == 6 && OffSeasonRules.HalfJobs(18) == 9 &&
                  OffSeasonRules.HalfJobs(8) == 4 && OffSeasonRules.HalfJobs(1) == 1 && OffSeasonRules.HalfJobs(5) == 3 &&
                  OffSeasonRules.HalfJobs(0) == 0 && OffSeasonRules.HalfJobs(-3) == 0, "OS4",
                  "HalfJobs rounds up: 4->2, 12->6, 18->9, 8->4, 1->1, 5->3, 0->0, -3->0");
            check(OffSeasonRules.Target(18, true, true) == 9 && OffSeasonRules.Target(18, true, false) == 18 &&
                  OffSeasonRules.Target(18, false, true) == 18 && OffSeasonRules.Target(-1, false, true) == 0, "OS4b",
                  "Target: half only off-season with the setting on; else full");
            LodgeDef aFrame = SkiCatalog.LodgeByPrefab(SkiCatalog.Names.LodgeAFrame), mid = SkiCatalog.LodgeByPrefab(SkiCatalog.Names.LodgeMid),
                     grand = SkiCatalog.LodgeByPrefab(SkiCatalog.Names.LodgeGrand), hut = SkiCatalog.LodgeByPrefab(SkiCatalog.Names.LodgeHut),
                     summit = SkiCatalog.LodgeByPrefab(SkiCatalog.Names.LodgeSummit);
            SchoolDef school = SkiCatalog.SchoolByPrefab(SkiCatalog.Names.School);
            check(OffSeasonRules.HalfJobs(aFrame.Jobs) == 2 && OffSeasonRules.HalfJobs(mid.Jobs) == 6 && OffSeasonRules.HalfJobs(grand.Jobs) == 9 &&
                  OffSeasonRules.HalfJobs(school.Jobs) == 2 && OffSeasonRules.HalfJobs(hut.SignJobs) == 2 && OffSeasonRules.HalfJobs(summit.SignJobs) == 4,
                  "OS4c", "off-season workplaces: A-frame 2, chalet 6, grand 9, school 2, hut (sign) 2, summit (sign) 4");

            check(OffSeasonRules.IsSeasonal(true, false) && OffSeasonRules.IsSeasonal(false, true) && !OffSeasonRules.IsSeasonal(false, false),
                  "OS5", "lodges and the school are seasonal; patrol (neither) is not");
            check(aFrame.Jobs == 4 && school.Jobs == 4 && school.Upkeep == 3000 && aFrame.Upkeep == 3000, "OS5b",
                  $"T137 C1 / C2 catalog: A-frame jobs {aFrame.Jobs} (4), school jobs {school.Jobs} (4) upkeep {school.Upkeep} (3,000)");
            check(OffSeasonRules.DefaultEnabled, "OS5c", "the setting defaults to on");

            // OS6: one game day per month, polled every 256 frames: days 0-5 closed, 6-11 open, 12 closed again
            s = default;
            bool offAtLoad = false;
            long firstOn = -1, secondOff = -1;
            for (uint f = 0; f < 13u * Day; f += 256u)
            {
                bool closed = f < 6u * Day || f >= 12u * Day;
                bool off = OffSeasonRules.Step(ref s, closed, f);
                if (f == 0u)
                    offAtLoad = off;
                if (!off && firstOn < 0)
                    firstOn = f;
                if (off && f >= 12u * Day && secondOff < 0)
                    secondOff = f;
            }
            check(offAtLoad, "OS6", "loaded in May (no open run): off-season at the first poll");
            check(firstOn >= 6L * Day + OffSeasonRules.HoldToOnFrames && firstOn < 6L * Day + OffSeasonRules.HoldToOnFrames + 256, "OS6b",
                  $"November's first open run: full staff one game hour later (frame {firstOn})");
            check(secondOff >= 12L * Day + OffSeasonRules.HoldToOffFrames && secondOff < 12L * Day + OffSeasonRules.HoldToOffFrames + 256, "OS6c",
                  $"the next May: half staff 12 game hours after the last run closed (frame {secondOff})");

            // OS7: sign jobs of the roadless buildings
            check(MountainJobsRules.SignJobsFor(summit.SignJobs, false, true, false) == 8 && MountainJobsRules.SignJobsFor(summit.SignJobs, false, true, true) == 4 &&
                  MountainJobsRules.SignJobsFor(hut.SignJobs, false, true, true) == 2, "OS7",
                  "summit 8 in season, 4 off-season; hut 2 off-season");
            check(MountainJobsRules.SignJobsFor(2, false, false, true) == 2, "OS7b", "a patrol row keeps its 2 off-season");
            check(MountainJobsRules.SignJobsFor(summit.SignJobs, true, true, false) == 0 && MountainJobsRules.SignJobsFor(2, true, false, false) == 0 &&
                  MountainJobsRules.SignJobsFor(0, false, true, true) == 0 && MountainJobsRules.SignJobsFor(-4, false, true, false) == 0, "OS7c",
                  "an Inactive building puts 0 on the sign (vanilla bills an Inactive building no wages, CityServiceUpkeepSystem.cs:630-633); 0 / negative = 0");
            var sums = new System.Collections.Generic.Dictionary<EntityRef, int>();
            var sign = new EntityRef(10, 1);
            MountainJobsRules.SumBySign(new[] { sign, sign, sign },
                new[] { MountainJobsRules.SignJobsFor(8, false, true, true), MountainJobsRules.SignJobsFor(4, true, true, true), MountainJobsRules.SignJobsFor(2, false, false, true) },
                new[] { true, true, true }, sums);
            check(sums.TryGetValue(sign, out int sum) && sum == 6, "OS7d",
                  $"off-season sign: summit 4 + an Inactive hut 0 + patrol hut 2 = {sum} (6)");
        }
    }
}
