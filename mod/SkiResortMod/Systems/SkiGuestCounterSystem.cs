// SkiGuestCounterSystem — counts the guests who arrive at a ski area (Round E1, decision D25;
// docs/research/econ-round/spec/ch4-money.md §4, §10): tourist parties through meetings, residents
// through leisure at a lodge (strict partition by household, DV8), written into the monthly
// SkiResortLedger and handed to SkiLiftTicketSystem as GuestArrival records (DrainArrivals).
// Stage 0 (S0) laid down the shell (e1-spec I-18); stream S4 wrote the system (2026-10-04).
//
// WHAT IS A GUEST (ch4 §4.1, DV8 — a narrowing of D25, stated):
//   * Tourist party: a CoordinatedMeeting whose m_Target is one of our targets and whose FIRST attendee's
//     household HAS TouristHousehold; counted at the first scan where the meeting is Attending and was
//     not Attending in the previous scan (MeetingSystem.cs:126-147: Attending = every attendee's
//     CurrentBuilding is the target; :149-178 a one-phase attraction outing with delay (0, 0) is
//     Attending for exactly one MeetingSystem update). People = the attendee buffer length (all members
//     of one household, AddMeetingSystem.cs:70-84). The prev-set rule makes a longer Attending spell count
//     once and a new spell (a multi-phase outing returning) count again.
//   * Resident: a citizen whose Leisure.m_TargetAgent is one of our LODGES and whose
//     CurrentBuilding.m_CurrentBuilding is that lodge, household WITHOUT TouristHousehold; counted at the
//     first leisure scan where it is on site and was not on site the scan before (set difference). A
//     tourist on an ordinary leisure visit is not counted (it may carry a Leisure aimed at the lodge its
//     outing took it to, LeisureSystem.cs 1.6.2 :375-403 — counting it would count it twice).
//   * The first scan of each kind after a load is a BASELINE: it records, counts nothing (a party or a
//     resident on site at load time was counted, and maybe charged, before the save). Additive (S4): a
//     target that joins the map later (a lodge switched back on with residents still inside — SpendLeisure
//     ends a visit at an inactive provider only on its own 1,024-frame pass, LeisureSystem.cs 1.6.2
//     :283-312) gets the same baseline at the next leisure scan, so nobody is counted or charged twice.
//
// TARGETS (ch4 §2.2, §4.2): every live Welcome Sign (SkiRunOwnershipSystem.IsGatePrefab) and every ACTIVE
// lodge listed in a live sign's InstalledUpgrade buffer (SkiResortAttractivenessSystem.IsLodgePrefab and
// !BuildingUtils.CheckOption(entry, BuildingOption.Inactive), BuildingUtils.cs:356). Free-standing and
// out-of-service lodges are not targets (counted for the targets line only). The map building → sign is
// rebuilt on the main thread between jobs when the Park query's entity count moves, when the attraction
// model's Generation moves (a lodge switched on or off changes no count), every Tuning.MapBackstopTicks
// ticks, and on the first tick after a load. Query {Building, Game.Buildings.Park, PrefabRef} None {Temp,
// Deleted}: sign and lodge both carry Game.Buildings.Park (Game.Prefabs/Park.cs:29). The rebuild syncs only
// PrefabRef's writers (CompleteDependencyBeforeRO, ParkedVehiclesSystem.cs:1732), never the Transform writers
// of the move systems (E1 fixer F7: Transform was in the query, so every rebuild waited on CarMoveSystem /
// HumanMoveSystem jobs; the map rebuilds on every attraction Generation move, up to every 256 frames).
//
// JOBS (ch4 §4.5): MeetingScanJob (every tick) and LeisureScanJob (every Tuning.LeisureEveryNthTick-th
// tick, scheduled after the meeting job; they share Stats) are single-threaded IJobs over
// ToArchetypeChunkListAsync chunk lists (the BrandPopularitySystem.cs:168-177 pattern), [BurstCompile]
// best-effort (the SkiSnowInjectionSystem convention: unburst they run as managed code on a worker).
// Their results are read on the NEXT tick after Complete(), which by then costs nothing — no main-thread
// wait on MeetingSystem's or TripNeededSystem's jobs. EarlyJobInit for both in OnCreate (the
// SkiRunDrawFeedbackSystem.PrewarmJit precedent). The dev counters (parties picked, locals heading, scan
// stats) are copied into managed fields after Complete, so DUMP / hub reads never touch a container a
// job may be writing.
//
// STEP ORDER per tick (ch4 §4.5): 1 complete the scans; 2 month (SkiMoneyRules.MonthKey of TimeSystem
// year / normalizedDate, TimeSystem.cs:38-40; Ledger.Advance; month lines and Prune on a roll or jump);
// 3 apply last tick's arrivals (ledger, pending list for the ticket system, first-arrival lines), swap
// the Attending / on-site sets, clear the baselines that ran; 4 rebuild the map when asked; 5 schedule.
// Arrivals detected in the last 16 frames of a month are booked into the new month (accepted, ch4 §4.5).
//
// LEDGER: this system owns the one SkiResortLedger (reset in OnGameLoadingComplete; only this system
// calls Advance). SkiLiftTicketSystem (AddTickets, AddLocalsPaid) and SkiLiftChargeSystem
// (AddLiftCharge) write into it through Ledger. AddLocalsPaid (additive, internal) keeps the per-ski-area
// count of residents who paid this month for the O1 note line (e1-spec §7.3 O1), which the ledger's
// tourist / local split of guests alone cannot give.
//
// PHASE: GameSimulation, anchored UpdateAfter<SkiGuestCounterSystem, Game.Simulation.MeetingSystem>
// (Mod.cs; load-bearing for freshness, not for exactly-once — ch4 §12.1; MeetingSystem is UpdateAt
// GameSimulation, SystemOrder.cs:404). Pause stops it; the Editor never arms it.
// CADENCE: GetUpdateInterval 16 = MeetingSystem's interval (MeetingSystem.cs:277-280), so the anchor's
// offset is inherited (UpdateSystem.cs:400-431).
// COST (ch4 §8; estimates, UNVERIFIED until the field): main thread per tick = Complete of jobs already
// done, apply (few arrivals), one CalculateEntityCount (O(chunks)), two schedules: tens of µs. Workers:
// meetings O(meeting entities) ~0.1 ms; leisure O(citizens out for leisure) 0.2-1 ms per 256 frames at
// 100k population. Measured once per load: `Guests [cost]` after Tuning.CostTicks ticks, plus one
// schedule-to-complete timing of both scans on the first leisure tick from tick CostTicks on (results
// used as on any other tick — nothing counted twice). Round F7 (item E3, log-audit D5): only ticks that
// scheduled the scans count (the map had a target); idle ticks before the first Welcome Sign are left out.
// LOGS (ch4 §4.7, exact): created; ARMED; `Guests [targets]` on change; `Guests [first]` per ski area per
// load (first tourist party, first local); `Guests [month]` per ski area + total at a roll, the jump line,
// the O1 note; the month in progress before a load (` (until the load)`) and before a jump
// (` (until the jump)`, additive); `Guests [cost]` once; `Guests [idle]` per transition; one WARN per load
// when arrivals pile up undrained (Tuning.PendingCap); the one-shot `!!!` (disabled for the load).
// No per-arrival lines.
// CONTRACT (house pattern): "created" line; per-load reset and re-arm in OnGameLoadingComplete (Game mode
// only; SkiOtherMods.Refresh runs there too); OnGamePreload completes jobs, books the finished scan and the
// ticket system's batch in flight (E1 fixer pass 2), and writes the month in progress (try/caught:
// GameSystemBase.cs:85-96 disables a system whose OnGamePreload throws); one-shot
// "!!!" and disabled for the load on an exception; OnDestroy completes and disposes native containers.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Purpose = Colossal.Serialization.Entities.Purpose;   // Game.Citizens has a Purpose too

namespace SkiResortMod.Systems
{
    /// <summary>One guest arrival (a tourist party or a resident household) at a ski area's building
    /// (ch4 §10). Sign is never Entity.Null for a counted arrival (only mapped targets are counted).</summary>
    public struct GuestArrival
    {
        public Entity Sign, Building, Household;
        public int People;
        public bool Tourist;
    }

    public sealed partial class SkiGuestCounterSystem : GameSystemBase
    {
        // ── Tuning (printed in the ARMED / cost lines where it matters) ───────────────────────────────
        private static class Tuning
        {
            /// <summary>One leisure scan per this many ticks (16 × 16 = 256 sim frames, ch4 §4.4).</summary>
            public const int LeisureEveryNthTick = 16;
            /// <summary>Map rebuild backstop (256 × 16 = 4,096 sim frames, ch4 §4.2).</summary>
            public const int MapBackstopTicks = 256;
            /// <summary>Ticks with scans timed for the `Guests [cost]` line (once per load, ch4 §4.7; F7 E3).</summary>
            public const int CostTicks = 256;
            /// <summary>O1 note threshold: the lodge break-even in paying locals a month (ch4 §4.7, e1-spec O1).</summary>
            public const int LocalTicketsNotePerMonth = 300;
            /// <summary>Undrained arrivals kept at most (the ticket system drains every 16 frames; more means it
            /// is not running). Above it the pending list is dropped with one WARN per load.</summary>
            public const int PendingCap = 4096;
        }

        private const string IdleNoSign = "no Welcome Sign";
        private const string IdleNotReady = "prefab sets not ready";

        // ── systems ───────────────────────────────────────────────────────────────────────────────
        private TimeSystem m_TimeSystem;
        private SimulationSystem m_SimulationSystem;
        private SkiRunOwnershipSystem m_Ownership;
        private SkiResortAttractivenessSystem m_Attractiveness;
        private Game.UI.NameSystem m_NameSystem;

        // ── queries, handles, lookups (registered in OnCreate, so no first-tick sync) ──────────────
        private EntityQuery m_MeetingQuery, m_LeisureQuery, m_TargetQuery;
        private EntityTypeHandle m_EntityType;
        private ComponentTypeHandle<CoordinatedMeeting> m_MeetingType;
        private BufferTypeHandle<CoordinatedMeetingAttendee> m_AttendeeType;
        private ComponentTypeHandle<Leisure> m_LeisureType;
        private ComponentTypeHandle<CurrentBuilding> m_CurrentBuildingType;
        private ComponentTypeHandle<HouseholdMember> m_HouseholdMemberType;
        private ComponentLookup<HouseholdMember> m_HouseholdMemberLookup;
        private ComponentLookup<TouristHousehold> m_TouristHouseholdLookup;

        // ── native state (Persistent; created in OnCreate, disposed in OnDestroy) ────────────────────
        private NativeParallelHashMap<Entity, Entity> m_TargetSign;
        private NativeParallelHashSet<Entity> m_AttendingBefore, m_AttendingNow;
        private NativeParallelHashSet<Entity> m_OnSiteBefore, m_OnSiteNow;
        private NativeParallelHashSet<Entity> m_NewTargets;   // targets new since the last leisure scan (per-target baseline)
        private NativeList<GuestArrival> m_Arrivals;
        private NativeParallelHashMap<Entity, int> m_PartiesPicked, m_LocalsHeading;
        private NativeArray<int> m_Stats;   // [0] meetings scanned, [1] attending at ours, [2] leisure citizens scanned, [3] residents on site
        private JobHandle m_ScanHandle;
        private bool m_MeetingInFlight, m_LeisureInFlight;

        // ── managed state ─────────────────────────────────────────────────────────────────────────
        private readonly SkiResortLedger m_Ledger = new SkiResortLedger();
        private readonly List<GuestArrival> m_Pending = new List<GuestArrival>(16);
        private readonly List<Entity> m_Signs = new List<Entity>(4);                 // live signs of the last rebuild
        private readonly HashSet<EntityRef> m_LiveSigns = new HashSet<EntityRef>();   // the same, for Prune
        private readonly Dictionary<Entity, int> m_PickedLast = new Dictionary<Entity, int>();
        private readonly Dictionary<Entity, int> m_HeadingLast = new Dictionary<Entity, int>();
        private readonly Dictionary<Entity, int> m_LocalsPaidThis = new Dictionary<Entity, int>();
        private readonly Dictionary<Entity, int> m_LocalsPaidLast = new Dictionary<Entity, int>();
        private readonly HashSet<Entity> m_FirstTourist = new HashSet<Entity>();
        private readonly HashSet<Entity> m_FirstLocal = new HashSet<Entity>();
        private readonly HashSet<Entity> m_O1Noted = new HashSet<Entity>();
        private readonly HashSet<Entity> m_ListedScratch = new HashSet<Entity>();
        private HashSet<Entity> m_Targets = new HashSet<Entity>();       // the map's keys (this rebuild / the previous one)
        private HashSet<Entity> m_TargetsPrev = new HashSet<Entity>();

        // ── per-load state ────────────────────────────────────────────────────────────────────────
        private bool m_Armed, m_Disabled, m_ErrorLogged, m_PendingWarned;
        private bool m_MeetingBaseline, m_LeisureBaseline;
        private bool m_MapValid;
        private int m_MapParkCount, m_MapGeneration, m_TicksSinceRebuild, m_Rebuilds;
        private int m_TargetSigns, m_TargetLodges, m_LodgesOff, m_LodgesFree;
        private string m_LastTargetsLine, m_IdleReason;
        private long m_Ticks;
        private int m_LastMeetings, m_LastAttendingOurs, m_LastLeisure, m_LastOnSite;
        private long m_ArrivalsTotal;

        // cost (once per load)
        private int m_CostTicks;
        private double m_CostSumMs, m_CostMaxMs;
        private bool m_CostLogged;

        /// <summary>The monthly ledger; never null; reset each load.</summary>
        internal SkiResortLedger Ledger => m_Ledger;

        /// <summary>Game mode, not disabled.</summary>
        internal bool IsArmed => m_Armed && !m_Disabled;

        /// <summary>For the stats change signature (= Ledger.Revision).</summary>
        internal int Revision => Ledger.Revision;

        /// <summary>Additive (S4): SimulationSystem.frameIndex when the last arrivals were appended to the
        /// pending list (the ticket system's lag figure in its DUMP, ch4 U14).</summary>
        internal uint LastPendingFrame { get; private set; }

        /// <summary>Additive (E1 fixer, e1-spec §8.9 F8): SimulationSystem.frameIndex when the ledger's "this
        /// month" started counting — the first tick after a load (Started) or a calendar jump (Jumped).
        /// HasCountingSinceFrame is false until then. The hub's projection (HubRules.CountedFraction) measures
        /// the counted part of a since-load month from here.</summary>
        internal uint CountingSinceFrame { get; private set; }
        internal bool HasCountingSinceFrame { get; private set; }

        /// <summary>Appends the pending arrivals to <paramref name="into"/> and clears them; returns the count.</summary>
        internal int DrainArrivals(List<GuestArrival> into)
        {
            int n = m_Pending.Count;
            if (n == 0)
                return 0;
            into.AddRange(m_Pending);
            m_Pending.Clear();
            return n;
        }

        /// <summary>This and last month of <paramref name="sign"/>; Entity.Null = Not linked.</summary>
        internal bool TryGetMonth(Entity sign, out SkiResortLedger.Entry entry)
        {
            return m_Ledger.TryGet(Ref(sign), out entry);
        }

        /// <summary>Dev / DUMP only: parties picked and locals heading to <paramref name="sign"/> in the last
        /// meeting / leisure scan. False when neither scan saw any for this sign.</summary>
        internal bool TryGetPicked(Entity sign, out int partiesPicked, out int localsHeading)
        {
            bool p = m_PickedLast.TryGetValue(sign, out partiesPicked);
            bool l = m_HeadingLast.TryGetValue(sign, out localsHeading);
            return p || l;
        }

        /// <summary>Additive (S4, the O1 note): SkiLiftTicketSystem books residents who paid at
        /// <paramref name="sign"/> into the month in progress. Main thread.</summary>
        internal void AddLocalsPaid(Entity sign, int paid)
        {
            if (paid <= 0 || sign == Entity.Null)
                return;
            m_LocalsPaidThis[sign] = (m_LocalsPaidThis.TryGetValue(sign, out int n) ? n : 0) + paid;
        }

        /// <summary>DUMP lines (appends only; never throws).</summary>
        internal void AppendDump(StringBuilder sb)
        {
            try
            {
                int key = m_Ledger.MonthKey;
                sb.Append("  Guests: armed ").Append(m_Armed).Append(", disabled ").Append(m_Disabled)
                  .Append(", month ").Append(key == SkiResortLedger.NoMonth ? "none" : MonthText(key))
                  .Append(m_Ledger.ThisMonthSinceLoad ? " (since load)" : "")
                  .Append(", last month ").Append(m_Ledger.LastMonthKnown ? (m_Ledger.LastMonthSinceLoad ? "known (since load)" : "known (full)") : "unknown")
                  .Append(", ticks ").Append(m_Ticks)
                  .Append(", targets ").Append(m_TargetSigns).Append(" sign(s) + ").Append(m_TargetLodges).Append(" lodge(s)")
                  .Append(" (out of service ").Append(m_LodgesOff).Append(", free-standing ").Append(m_LodgesFree)
                  .Append("; rebuilds ").Append(m_Rebuilds).Append(", generation ").Append(m_MapGeneration).Append(')')
                  .Append(", last scans: ").Append(m_LastMeetings).Append(" meetings (").Append(m_LastAttendingOurs)
                  .Append(" attending at ours), ").Append(m_LastLeisure).Append(" leisure citizens (").Append(m_LastOnSite)
                  .Append(" residents on site), baselines meeting ").Append(m_MeetingBaseline).Append(" leisure ").Append(m_LeisureBaseline)
                  .Append(", arrivals this load ").Append(m_ArrivalsTotal).Append(", pending ").Append(m_Pending.Count)
                  .Append(", ledger revision ").Append(m_Ledger.Revision)
                  .Append(", idle '").Append(m_IdleReason ?? "").Append("'\n");
                for (int i = 0; i < m_Ledger.Count; i++)
                {
                    SkiResortLedger.Entry e = m_Ledger.At(i);
                    if (e.Sign.IsNull)
                    {
                        sb.Append("    not linked: lift charges this month -").Append(e.This.LiftCharged.ToString("F0"))
                          .Append(", last month -").Append(e.Last.LiftCharged.ToString("F0")).Append('\n');
                        continue;
                    }
                    Entity sign = Ent(e.Sign);
                    TryGetPicked(sign, out int picked, out int heading);
                    m_LocalsPaidThis.TryGetValue(sign, out int localsPaid);
                    m_LocalsPaidLast.TryGetValue(sign, out int localsPaidLast);
                    sb.Append("    ski area ").Append(Fmt(e.Sign)).Append(NameSuffix(sign))
                      .Append(": this month ").Append(MonthBody(e.This))
                      .Append(" (locals paid ").Append(localsPaid).Append(')')
                      .Append(" | last month ").Append(m_Ledger.LastMonthKnown ? MonthBody(e.Last) + " (locals paid " + localsPaidLast + ")" : "unknown")
                      .Append(" | picked by ").Append(picked).Append(" parties, ").Append(heading).Append(" locals heading\n");
                }
            }
            catch (Exception e)
            {
                sb.Append("  Guests: dump failed (").Append(e.GetType().Name).Append(": ").Append(e.Message).Append(")\n");
            }
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            m_TimeSystem = World.GetOrCreateSystemManaged<TimeSystem>();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_Ownership = World.GetOrCreateSystemManaged<SkiRunOwnershipSystem>();
            m_Attractiveness = World.GetOrCreateSystemManaged<SkiResortAttractivenessSystem>();
            m_NameSystem = World.GetOrCreateSystemManaged<Game.UI.NameSystem>();

            m_MeetingQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<CoordinatedMeeting>(),
                    ComponentType.ReadOnly<CoordinatedMeetingAttendee>(),
                },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });
            m_LeisureQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Citizen>(),
                    ComponentType.ReadOnly<Leisure>(),
                    ComponentType.ReadOnly<HouseholdMember>(),
                },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });
            m_TargetQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Building>(),
                    ComponentType.ReadOnly<Game.Buildings.Park>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    // E1 fixer (e1-spec §8.9 F7): no Transform here. The walk never reads it, and a query's
                    // CompleteDependency completes the writers of every type it names — Transform is written
                    // every frame by CarMoveSystem / HumanMoveSystem jobs scheduled just before MeetingSystem.
                },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });

            m_EntityType = GetEntityTypeHandle();
            m_MeetingType = GetComponentTypeHandle<CoordinatedMeeting>(isReadOnly: true);
            m_AttendeeType = GetBufferTypeHandle<CoordinatedMeetingAttendee>(isReadOnly: true);
            m_LeisureType = GetComponentTypeHandle<Leisure>(isReadOnly: true);
            m_CurrentBuildingType = GetComponentTypeHandle<CurrentBuilding>(isReadOnly: true);
            m_HouseholdMemberType = GetComponentTypeHandle<HouseholdMember>(isReadOnly: true);
            m_HouseholdMemberLookup = GetComponentLookup<HouseholdMember>(isReadOnly: true);
            m_TouristHouseholdLookup = GetComponentLookup<TouristHousehold>(isReadOnly: true);

            m_TargetSign = new NativeParallelHashMap<Entity, Entity>(16, Allocator.Persistent);
            m_AttendingBefore = new NativeParallelHashSet<Entity>(16, Allocator.Persistent);
            m_AttendingNow = new NativeParallelHashSet<Entity>(16, Allocator.Persistent);
            m_OnSiteBefore = new NativeParallelHashSet<Entity>(64, Allocator.Persistent);
            m_OnSiteNow = new NativeParallelHashSet<Entity>(64, Allocator.Persistent);
            m_NewTargets = new NativeParallelHashSet<Entity>(8, Allocator.Persistent);
            m_Arrivals = new NativeList<GuestArrival>(16, Allocator.Persistent);
            m_PartiesPicked = new NativeParallelHashMap<Entity, int>(8, Allocator.Persistent);
            m_LocalsHeading = new NativeParallelHashMap<Entity, int>(8, Allocator.Persistent);
            m_Stats = new NativeArray<int>(4, Allocator.Persistent);

            // Job reflection data and JIT before the first Schedule (the PrewarmJit precedent of
            // SkiRunDrawFeedbackSystem; ch4 §4.5). Process-wide; schedules nothing.
            IJobExtensions.EarlyJobInit<MeetingScanJob>();
            IJobExtensions.EarlyJobInit<LeisureScanJob>();

            Mod.Log.Info($"{nameof(SkiGuestCounterSystem)} created");
        }

        /// <summary>MeetingSystem's interval (header CADENCE): the anchored offset is inherited.</summary>
        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return 16;
        }

        protected override void OnDestroy()
        {
            try
            {
                m_ScanHandle.Complete();
            }
            catch (Exception)
            {
                // teardown: nothing left to report to
            }
            if (m_TargetSign.IsCreated) m_TargetSign.Dispose();
            if (m_AttendingBefore.IsCreated) m_AttendingBefore.Dispose();
            if (m_AttendingNow.IsCreated) m_AttendingNow.Dispose();
            if (m_OnSiteBefore.IsCreated) m_OnSiteBefore.Dispose();
            if (m_OnSiteNow.IsCreated) m_OnSiteNow.Dispose();
            if (m_NewTargets.IsCreated) m_NewTargets.Dispose();
            if (m_Arrivals.IsCreated) m_Arrivals.Dispose();
            if (m_PartiesPicked.IsCreated) m_PartiesPicked.Dispose();
            if (m_LocalsHeading.IsCreated) m_LocalsHeading.Dispose();
            if (m_Stats.IsCreated) m_Stats.Dispose();
            base.OnDestroy();
        }

        /// <summary>A load (or leaving to the menu): completes the scans, books their arrivals, lets the ticket
        /// system book its batch in flight, then writes the month in progress (` (until the load)`). Never
        /// throws: GameSystemBase.GamePreload DISABLES a system whose OnGamePreload throws
        /// (Game/GameSystemBase.cs:85-96).
        /// E1 fixer pass 2 (e1-spec §8.10 G8): the finished scan used to be dropped (flags cleared without
        /// ApplyScans), and the line was written before SkiLiftTicketSystem.OnGamePreload booked its last job:
        /// onGamePreload runs in subscription order, GameSystemBase.OnCreate subscribes (GameSystemBase.cs:28),
        /// and this system is created first (Mod.cs: the counter's UpdateAfter precedes the ticket's).
        /// The game pauses when the menu opens, so the last tick's scan and ticket job are always in flight at
        /// a load. The arrivals booked here land in m_Pending, which the load clears: counted, never charged
        /// (the world is replaced).</summary>
        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            try
            {
                m_ScanHandle.Complete();
                if (m_Armed && !m_Disabled)
                    ApplyScans();   // books the finished scan (no-op when nothing was in flight)
                m_MeetingInFlight = m_LeisureInFlight = false;
                if (m_Armed && !m_Disabled)
                    World.GetExistingSystemManaged<SkiLiftTicketSystem>()?.BookInFlightForPreload();
                if (m_Armed && m_Ledger.MonthKey != SkiResortLedger.NoMonth)
                    WriteMonthLines(m_Ledger.MonthKey, last: false, m_Ledger.ThisMonthSinceLoad, " (until the load)");
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"Guests [month]: the month in progress could not be written before the load " +
                             $"({e.GetType().Name}: {e.Message}) — non-fatal");
            }
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            m_ScanHandle.Complete();
            m_MeetingInFlight = m_LeisureInFlight = false;
            m_Armed = false;
            m_Disabled = false;
            m_ErrorLogged = false;
            m_PendingWarned = false;
            m_MeetingBaseline = true;
            m_LeisureBaseline = true;
            m_MapValid = false;
            m_MapParkCount = -1;
            m_MapGeneration = 0;
            m_TicksSinceRebuild = 0;
            m_Rebuilds = 0;
            m_TargetSigns = m_TargetLodges = m_LodgesOff = m_LodgesFree = 0;
            m_LastTargetsLine = null;
            m_IdleReason = null;
            m_Ticks = 0;
            m_LastMeetings = m_LastAttendingOurs = m_LastLeisure = m_LastOnSite = 0;
            m_ArrivalsTotal = 0;
            m_CostTicks = 0;
            m_CostSumMs = m_CostMaxMs = 0.0;
            m_CostLogged = false;
            LastPendingFrame = 0;
            CountingSinceFrame = 0;
            HasCountingSinceFrame = false;

            m_TargetSign.Clear();
            m_AttendingBefore.Clear();
            m_AttendingNow.Clear();
            m_OnSiteBefore.Clear();
            m_OnSiteNow.Clear();
            m_NewTargets.Clear();
            m_Arrivals.Clear();
            m_PartiesPicked.Clear();
            m_LocalsHeading.Clear();
            for (int i = 0; i < m_Stats.Length; i++)
                m_Stats[i] = 0;
            m_Pending.Clear();
            m_Targets.Clear();
            m_TargetsPrev.Clear();
            m_Signs.Clear();
            m_LiveSigns.Clear();
            m_PickedLast.Clear();
            m_HeadingLast.Clear();
            m_LocalsPaidThis.Clear();
            m_LocalsPaidLast.Clear();
            m_FirstTourist.Clear();
            m_FirstLocal.Clear();
            m_O1Noted.Clear();
            m_Ledger.Reset();

            // Labels only (ch4 §6.5): every mod assembly is loaded by now.
            SkiOtherMods.Refresh();

            if (mode != GameMode.Game)
                return;

            m_Armed = true;
            int dpy = m_TimeSystem.daysPerYear;
            Mod.Log.Info("Guests: ARMED — tourist parties counted once when their meeting is Attending at a Welcome Sign " +
                         "or lodge (scan every 16 sim frames after MeetingSystem); residents counted when they first stand " +
                         $"in a lodge they chose for leisure (scan every {16 * Tuning.LeisureEveryNthTick} sim frames, set " +
                         "difference); first scan after a load is a baseline; month = the game's calendar month " +
                         $"({dpy} days per year → {SkiMoneyRules.MonthFrames(dpy):F0} sim frames); in memory, reset on load");
        }

        protected override void OnUpdate()
        {
            try
            {
                if (m_Disabled || !m_Armed)
                    return;
                long t0 = Stopwatch.GetTimestamp();

                // 1. Last tick's scans (done by now: 16 frames ago).
                m_ScanHandle.Complete();

                // 2. Month.
                AdvanceMonth();

                // 3. Apply last tick's arrivals.
                ApplyScans();

                // 4. The target map.
                MaybeRebuildMap();

                // 5. Schedule this tick's scans.
                bool leisureTick = m_Ticks % Tuning.LeisureEveryNthTick == 0;
                bool scheduled = Schedule(leisureTick);
                m_Ticks++;

                double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                RecordCost(ms, scheduled, leisureTick && scheduled);
            }
            catch (Exception e)
            {
                m_Disabled = true;
                try
                {
                    m_ScanHandle.Complete();
                }
                catch (Exception)
                {
                    // the first exception is the one worth reporting
                }
                m_MeetingInFlight = m_LeisureInFlight = false;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiGuestCounterSystem)} EXCEPTION — guest counting disabled until the next " +
                                  $"game load (tickets stop with it). Please report this stack:\n{e}");
                }
            }
        }

        // ── 2. month ──────────────────────────────────────────────────────────────────────────────

        private void AdvanceMonth()
        {
            int key = SkiMoneyRules.MonthKey(m_TimeSystem.year, m_TimeSystem.normalizedDate);
            int prev = m_Ledger.MonthKey;
            bool jump = prev != SkiResortLedger.NoMonth && key != prev && !(prev != int.MaxValue && key == prev + 1);
            if (jump)
                WriteMonthLines(prev, last: false, m_Ledger.ThisMonthSinceLoad, " (until the jump)");   // additive: nothing is lost silently
            switch (m_Ledger.Advance(key))
            {
                case SkiResortLedger.AdvanceResult.Started:
                    CountingSinceFrame = m_SimulationSystem.frameIndex;   // E1 fixer F8
                    HasCountingSinceFrame = true;
                    break;
                case SkiResortLedger.AdvanceResult.Rolled:
                    WriteMonthLines(prev, last: true, m_Ledger.LastMonthSinceLoad, "");
                    RollLocalsPaid(rolled: true);
                    m_Ledger.Prune(m_LiveSigns);
                    break;
                case SkiResortLedger.AdvanceResult.Jumped:
                    // E1 fixer F8: counting restarts here, partway through the month (the ledger marks it
                    // since-load), so the hub's projection measures from this frame.
                    CountingSinceFrame = m_SimulationSystem.frameIndex;
                    HasCountingSinceFrame = true;
                    Mod.Log.Info($"Guests [month] the calendar jumped from {MonthText(prev)} to {MonthText(key)} (time changed) — " +
                                 "last month unknown; counting restarts");
                    RollLocalsPaid(rolled: false);
                    m_Ledger.Prune(m_LiveSigns);
                    break;
            }
        }

        private void RollLocalsPaid(bool rolled)
        {
            m_LocalsPaidLast.Clear();
            if (rolled)
            {
                foreach (var pair in m_LocalsPaidThis)
                    m_LocalsPaidLast[pair.Key] = pair.Value;
            }
            m_LocalsPaidThis.Clear();
        }

        /// <summary>ch4 §4.7: one line per ski area that has an entry, then the total line. <paramref name="last"/>
        /// = the closed month (Entry.Last, after a roll) and the O1 note; else the month in progress (Entry.This).</summary>
        private void WriteMonthLines(int key, bool last, bool sinceLoad, string suffix)
        {
            string month = MonthText(key) + (sinceLoad ? " (since load)" : "") + suffix;
            int guests = 0;
            long income = 0;
            double lift = 0.0, notLinked = 0.0;
            for (int i = 0; i < m_Ledger.Count; i++)
            {
                SkiResortLedger.Entry e = m_Ledger.At(i);
                SkiResortLedger.Month m = last ? e.Last : e.This;
                if (e.Sign.IsNull)
                {
                    notLinked += m.LiftCharged;
                    continue;
                }
                guests += m.Guests;
                income += m.TicketIncome;
                lift += m.LiftCharged;
                Entity sign = Ent(e.Sign);
                Mod.Log.Info($"Guests [month] {month} ski area {Fmt(e.Sign)}{NameSuffix(sign)}: {MonthBody(m)}");
            }
            int parties = 0, locals = 0;
            foreach (var pair in m_PickedLast)
                parties += pair.Value;
            foreach (var pair in m_HeadingLast)
                locals += pair.Value;
            Mod.Log.Info($"Guests [month] {month} all ski areas: guests {guests}, tickets +{income}, lift charges -{lift:F0}, " +
                         $"not linked lift charges -{notLinked:F0}; now picked by {parties} tourist part(ies) on the way, " +
                         $"{locals} local(s) heading to a lodge");

            if (!last)
                return;
            // O1 signal (e1-spec §7.3 O1): at most once per ski area per load; no behaviour change.
            foreach (var pair in m_LocalsPaidThis)
            {
                if (pair.Value <= Tuning.LocalTicketsNotePerMonth || m_O1Noted.Contains(pair.Key))
                    continue;
                m_O1Noted.Add(pair.Key);
                Mod.Log.Info($"Guests [month] ski area {Fmt(pair.Key)}: {pair.Value} local guest(s) paid tickets this month — " +
                             $"above LocalTicketsNotePerMonth={Tuning.LocalTicketsNotePerMonth} (the lodge break-even); see econ-round O1");
            }
        }

        private static string MonthBody(SkiResortLedger.Month m)
        {
            return $"guests {m.Guests} (tourists {m.TouristGuests} in {m.TouristParties} parties, locals {m.LocalGuests}); " +
                   $"tickets {m.TicketsPaid} paid +{m.TicketIncome}, {m.TicketsUnpaid} could not pay, {m.TicketsFree} free; " +
                   $"lift charges -{m.LiftCharged:F0}";
        }

        // ── 3. apply ──────────────────────────────────────────────────────────────────────────────

        private void ApplyScans()
        {
            if (!m_MeetingInFlight && !m_LeisureInFlight)
                return;

            for (int i = 0; i < m_Arrivals.Length; i++)
            {
                GuestArrival a = m_Arrivals[i];
                m_Ledger.AddArrival(Ref(a.Sign), a.Tourist, a.People);
                m_Pending.Add(a);
                m_ArrivalsTotal++;
                FirstArrivalLine(a);
            }
            if (m_Arrivals.Length > 0)
                LastPendingFrame = m_SimulationSystem.frameIndex;
            m_Arrivals.Clear();
            if (m_Pending.Count > Tuning.PendingCap)
            {
                if (!m_PendingWarned)
                {
                    m_PendingWarned = true;
                    Mod.Log.Warn($"Guests: {m_Pending.Count} arrival(s) were never drained for tickets (is SkiLiftTicketSystem " +
                                 $"running? see its !!! line) — dropped above PendingCap={Tuning.PendingCap}; the guest counts " +
                                 "stay right (warned once per load)");
                }
                m_Pending.Clear();
            }

            if (m_MeetingInFlight)
            {
                m_MeetingInFlight = false;
                m_MeetingBaseline = false;
                SwapSets(ref m_AttendingBefore, ref m_AttendingNow);
                m_AttendingNow.Clear();
                m_LastMeetings = m_Stats[0];
                m_LastAttendingOurs = m_Stats[1];
                CopyCounts(m_PartiesPicked, m_PickedLast);
            }
            if (m_LeisureInFlight)
            {
                m_LeisureInFlight = false;
                m_LeisureBaseline = false;
                m_NewTargets.Clear();   // their per-target baseline ran with this scan
                SwapSets(ref m_OnSiteBefore, ref m_OnSiteNow);
                m_OnSiteNow.Clear();
                m_LastLeisure = m_Stats[2];
                m_LastOnSite = m_Stats[3];
                CopyCounts(m_LocalsHeading, m_HeadingLast);
            }
        }

        private static void SwapSets(ref NativeParallelHashSet<Entity> a, ref NativeParallelHashSet<Entity> b)
        {
            NativeParallelHashSet<Entity> t = a;
            a = b;
            b = t;
        }

        private static void CopyCounts(NativeParallelHashMap<Entity, int> from, Dictionary<Entity, int> into)
        {
            into.Clear();
            if (from.Count() == 0)
                return;
            using (NativeKeyValueArrays<Entity, int> kv = from.GetKeyValueArrays(Allocator.Temp))
            {
                for (int i = 0; i < kv.Length; i++)
                    into[kv.Keys[i]] = kv.Values[i];
            }
        }

        private void FirstArrivalLine(GuestArrival a)
        {
            HashSet<Entity> seen = a.Tourist ? m_FirstTourist : m_FirstLocal;
            if (!seen.Add(a.Sign))
                return;
            string who = a.Tourist ? $"tourist party of {a.People}" : "local guest";
            string where = a.Building == a.Sign ? "sign" : "lodge";
            Mod.Log.Info($"Guests [first] ski area {Fmt(a.Sign)}{NameSuffix(a.Sign)}: first {who} this load at {where} {Fmt(a.Building)}");
        }

        // ── 4. the target map ─────────────────────────────────────────────────────────────────────

        private void MaybeRebuildMap()
        {
            m_TicksSinceRebuild++;
            int parkCount = m_TargetQuery.CalculateEntityCount();
            int generation = m_Attractiveness.Generation;
            if (m_MapValid && parkCount == m_MapParkCount && generation == m_MapGeneration &&
                m_TicksSinceRebuild < Tuning.MapBackstopTicks)
                return;
            m_MapValid = true;
            m_MapParkCount = parkCount;
            m_MapGeneration = generation;
            m_TicksSinceRebuild = 0;
            m_Rebuilds++;
            RebuildMap();
        }

        /// <summary>ch4 §4.2: signs map to themselves, active listed lodges to their sign. Main thread, between
        /// jobs (step 1 completed them).</summary>
        private void RebuildMap()
        {
            HashSet<Entity> swap = m_TargetsPrev;
            m_TargetsPrev = m_Targets;
            m_Targets = swap;
            m_Targets.Clear();
            m_TargetSign.Clear();
            m_Signs.Clear();
            m_LiveSigns.Clear();
            m_ListedScratch.Clear();
            m_TargetSigns = m_TargetLodges = m_LodgesOff = m_LodgesFree = 0;

            if (!m_Ownership.PrefabSetsReady)
            {
                SetIdle(IdleNotReady);
                return;
            }

            int lodgeBuildings = 0;
            if (!m_TargetQuery.IsEmptyIgnoreFilter)
            {
                // E1 fixer (F7): sync only what the walk reads — PrefabRef (Entity needs no sync; Building
                // and Park only select chunks). Vanilla does the same (ParkedVehiclesSystem.cs:1732).
                EntityManager.CompleteDependencyBeforeRO<PrefabRef>();
                EntityTypeHandle entityHandle = GetEntityTypeHandle();
                ComponentTypeHandle<PrefabRef> prefabHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
                using (NativeArray<ArchetypeChunk> chunks = m_TargetQuery.ToArchetypeChunkArray(Allocator.Temp))
                {
                    for (int c = 0; c < chunks.Length; c++)
                    {
                        NativeArray<Entity> entities = chunks[c].GetNativeArray(entityHandle);
                        NativeArray<PrefabRef> prefabs = chunks[c].GetNativeArray(ref prefabHandle);
                        for (int i = 0; i < entities.Length; i++)
                        {
                            Entity prefab = prefabs[i].m_Prefab;
                            if (m_Ownership.IsGatePrefab(prefab))
                                m_Signs.Add(entities[i]);
                            else if (m_Attractiveness.IsLodgePrefab(prefab))
                                lodgeBuildings++;
                        }
                    }
                }
            }

            for (int s = 0; s < m_Signs.Count; s++)
            {
                Entity sign = m_Signs[s];
                m_TargetSign.TryAdd(sign, sign);
                m_Targets.Add(sign);
                m_LiveSigns.Add(Ref(sign));
                if (!EntityManager.HasBuffer<InstalledUpgrade>(sign))
                    continue;
                DynamicBuffer<InstalledUpgrade> upgrades = EntityManager.GetBuffer<InstalledUpgrade>(sign, isReadOnly: true);
                for (int u = 0; u < upgrades.Length; u++)
                {
                    InstalledUpgrade entry = upgrades[u];
                    Entity lodge = entry.m_Upgrade;
                    if (!EntityManager.TryGetComponent(lodge, out PrefabRef pr) || !m_Attractiveness.IsLodgePrefab(pr.m_Prefab))
                        continue;
                    if (!m_ListedScratch.Add(lodge))
                        continue;   // listed twice (should not happen): counted once
                    if (BuildingUtils.CheckOption(entry, BuildingOption.Inactive))
                    {
                        m_LodgesOff++;   // out of service: not a target (ch4 §4.2, second pass)
                        continue;
                    }
                    if (m_TargetSign.TryAdd(lodge, sign))
                    {
                        m_TargetLodges++;
                        m_Targets.Add(lodge);
                    }
                }
            }
            // Per-target baseline (additive, notes-S4): a building that was not a target in the previous map
            // (a lodge switched back on, a new sign) gets a baseline at the next leisure scan, so residents
            // already inside it are recorded, not counted (and not charged) as new arrivals.
            foreach (Entity t in m_Targets)
            {
                if (!m_TargetsPrev.Contains(t))
                    m_NewTargets.Add(t);
            }
            m_TargetSigns = m_Signs.Count;
            m_LodgesFree = Math.Max(0, lodgeBuildings - m_ListedScratch.Count);

            if (m_TargetSigns == 0)
            {
                SetIdle(IdleNoSign);
                return;
            }
            m_IdleReason = null;
            string line = $"Guests [targets] {m_TargetSigns} sign(s), {m_TargetLodges} lodge(s) counted" +
                          (m_LodgesOff > 0 ? $"; out of service ignored: {m_LodgesOff}" : "") +
                          (m_LodgesFree > 0 ? $"; free-standing lodges ignored: {m_LodgesFree}" : "");
            if (line != m_LastTargetsLine)
            {
                m_LastTargetsLine = line;
                Mod.Log.Info(line);
            }
        }

        private void SetIdle(string reason)
        {
            m_LastTargetsLine = null;   // the next non-idle map is logged again
            if (m_IdleReason == reason)
                return;
            m_IdleReason = reason;
            Mod.Log.Info($"Guests [idle]: {reason} — nothing counted");
        }

        // ── 5. schedule ───────────────────────────────────────────────────────────────────────────

        /// <summary>Schedules the meeting scan (and the leisure scan on a leisure tick). Nothing while the map
        /// is empty (idle). Returns whether anything was scheduled.</summary>
        private bool Schedule(bool leisureTick)
        {
            if (m_TargetSign.Count() == 0)
                return false;

            m_AttendingNow.Clear();
            m_Arrivals.Clear();
            m_PartiesPicked.Clear();
            m_Stats[0] = 0;
            m_Stats[1] = 0;
            if (leisureTick)
            {
                m_OnSiteNow.Clear();
                m_LocalsHeading.Clear();
                m_Stats[2] = 0;
                m_Stats[3] = 0;
            }

            m_EntityType.Update(ref CheckedStateRef);
            m_MeetingType.Update(ref CheckedStateRef);
            m_AttendeeType.Update(ref CheckedStateRef);
            m_HouseholdMemberLookup.Update(this);
            m_TouristHouseholdLookup.Update(this);

            NativeList<ArchetypeChunk> meetingChunks = m_MeetingQuery.ToArchetypeChunkListAsync(Allocator.TempJob, out JobHandle meetingChunksHandle);
            var meetingJob = new MeetingScanJob
            {
                Chunks = meetingChunks,
                EntityType = m_EntityType,
                MeetingType = m_MeetingType,
                AttendeeType = m_AttendeeType,
                HouseholdMembers = m_HouseholdMemberLookup,
                TouristHouseholds = m_TouristHouseholdLookup,
                TargetSign = m_TargetSign,
                AttendingBefore = m_AttendingBefore,
                AttendingNow = m_AttendingNow,
                Arrivals = m_Arrivals,
                PartiesPicked = m_PartiesPicked,
                Stats = m_Stats,
                Baseline = m_MeetingBaseline,
            };
            JobHandle handle = meetingJob.Schedule(JobHandle.CombineDependencies(Dependency, meetingChunksHandle));
            meetingChunks.Dispose(handle);
            m_MeetingInFlight = true;

            if (leisureTick)
            {
                m_LeisureType.Update(ref CheckedStateRef);
                m_CurrentBuildingType.Update(ref CheckedStateRef);
                m_HouseholdMemberType.Update(ref CheckedStateRef);
                NativeList<ArchetypeChunk> leisureChunks = m_LeisureQuery.ToArchetypeChunkListAsync(Allocator.TempJob, out JobHandle leisureChunksHandle);
                var leisureJob = new LeisureScanJob
                {
                    Chunks = leisureChunks,
                    EntityType = m_EntityType,
                    LeisureType = m_LeisureType,
                    CurrentBuildingType = m_CurrentBuildingType,
                    HouseholdMemberType = m_HouseholdMemberType,
                    TouristHouseholds = m_TouristHouseholdLookup,
                    TargetSign = m_TargetSign,
                    OnSiteBefore = m_OnSiteBefore,
                    NewTargets = m_NewTargets,
                    OnSiteNow = m_OnSiteNow,
                    Arrivals = m_Arrivals,
                    LocalsHeading = m_LocalsHeading,
                    Stats = m_Stats,
                    Baseline = m_LeisureBaseline,
                };
                handle = leisureJob.Schedule(JobHandle.CombineDependencies(handle, leisureChunksHandle));
                leisureChunks.Dispose(handle);
                m_LeisureInFlight = true;
            }

            Dependency = handle;
            m_ScanHandle = handle;
            return true;
        }

        // ── cost (once per load) ──────────────────────────────────────────────────────────────────

        /// <summary>`Guests [cost]`, once per load. F7 E3 (header COST): only ticks that scheduled the scans
        /// count — a tick with an empty target map does no work (field test 7: 256 ticks were timed before
        /// the city had a Welcome Sign).</summary>
        private void RecordCost(double ms, bool scheduled, bool leisureScheduled)
        {
            if (m_CostLogged || !scheduled)
                return;
            if (m_CostTicks < Tuning.CostTicks)
            {
                m_CostTicks++;
                m_CostSumMs += ms;
                if (ms > m_CostMaxMs)
                    m_CostMaxMs = ms;
                return;
            }
            if (!leisureScheduled)
                return;   // wait for the first leisure tick from CostTicks on that scheduled both scans
            // ch4 §4.5 (second pass): complete THIS tick's scans at once, timed — schedule-to-complete on the
            // main thread. The results are applied next tick as on any other tick (nothing counted twice).
            long t0 = Stopwatch.GetTimestamp();
            m_ScanHandle.Complete();
            double w = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            m_CostLogged = true;
            Mod.Log.Info($"Guests [cost] main thread per tick: mean {m_CostSumMs / Math.Max(1, m_CostTicks):F3} ms, max {m_CostMaxMs:F3} ms " +
                         $"over {m_CostTicks} ticks with scans; last scans: {m_Stats[0]} meetings, {m_Stats[2]} leisure citizens; targets " +
                         $"{m_TargetSigns} signs + {m_TargetLodges} lodges; one synchronous scan (schedule to complete): " +
                         $"meetings + leisure {w:F3} ms");
        }

        // ── helpers ───────────────────────────────────────────────────────────────────────────────

        private string NameSuffix(Entity e)
        {
            if (e == Entity.Null || !m_NameSystem.TryGetCustomName(e, out string name) || string.IsNullOrEmpty(name))
                return "";
            return " '" + name + "'";
        }

        private static string MonthText(int key)
        {
            return SkiMoneyRules.YearOfKey(key) + "-" + SkiMoneyRules.MonthOfKey(key).ToString("D2");
        }

        private static EntityRef Ref(Entity e) => new EntityRef(e.Index, e.Version);
        private static Entity Ent(EntityRef r) => new Entity { Index = r.Index, Version = r.Version };
        private static string Fmt(Entity e) => e.Index + ":" + e.Version;
        private static string Fmt(EntityRef r) => r.Index + ":" + r.Version;

        // ── jobs (ch4 §4.5) ───────────────────────────────────────────────────────────────────────

        /// <summary>Tourist parties: a meeting at one of our targets, first attendee's household a tourist
        /// household; Attending and not Attending in the previous scan → one arrival (§4.3 exactly once).
        /// Traveling only feeds the dev counter "parties that picked this ski area" (a preset target turns
        /// Traveling before anyone has a path, AddMeetingSystem.cs:85-91).</summary>
        [BurstCompile]
        private struct MeetingScanJob : IJob
        {
            [ReadOnly] public NativeList<ArchetypeChunk> Chunks;
            [ReadOnly] public EntityTypeHandle EntityType;
            [ReadOnly] public ComponentTypeHandle<CoordinatedMeeting> MeetingType;
            [ReadOnly] public BufferTypeHandle<CoordinatedMeetingAttendee> AttendeeType;
            [ReadOnly] public ComponentLookup<HouseholdMember> HouseholdMembers;
            [ReadOnly] public ComponentLookup<TouristHousehold> TouristHouseholds;
            [ReadOnly] public NativeParallelHashMap<Entity, Entity> TargetSign;
            [ReadOnly] public NativeParallelHashSet<Entity> AttendingBefore;
            public NativeParallelHashSet<Entity> AttendingNow;          // cleared by the system before scheduling
            public NativeList<GuestArrival> Arrivals;                   // cleared by the system before scheduling
            public NativeParallelHashMap<Entity, int> PartiesPicked;    // dev: Traveling parties per sign
            public NativeArray<int> Stats;                              // [0] meetings scanned, [1] attending at ours
            public bool Baseline;

            public void Execute()
            {
                for (int c = 0; c < Chunks.Length; c++)
                {
                    ArchetypeChunk chunk = Chunks[c];
                    NativeArray<Entity> entities = chunk.GetNativeArray(EntityType);
                    NativeArray<CoordinatedMeeting> meetings = chunk.GetNativeArray(ref MeetingType);
                    BufferAccessor<CoordinatedMeetingAttendee> parties = chunk.GetBufferAccessor(ref AttendeeType);
                    Stats[0] += entities.Length;
                    for (int i = 0; i < entities.Length; i++)
                    {
                        CoordinatedMeeting m = meetings[i];
                        if (m.m_Status != MeetingStatus.Attending && m.m_Status != MeetingStatus.Traveling)
                            continue;
                        if (!TargetSign.TryGetValue(m.m_Target, out Entity sign))
                            continue;
                        DynamicBuffer<CoordinatedMeetingAttendee> party = parties[i];
                        if (party.Length == 0)
                            continue;
                        if (!HouseholdMembers.TryGetComponent(party[0].m_Attendee, out HouseholdMember hm))
                            continue;
                        if (!TouristHouseholds.HasComponent(hm.m_Household))
                            continue;                                                       // §4.1 partition
                        if (m.m_Status == MeetingStatus.Traveling)
                        {
                            Bump(PartiesPicked, sign);
                            continue;
                        }
                        Stats[1]++;
                        AttendingNow.Add(entities[i]);
                        if (Baseline || AttendingBefore.Contains(entities[i]))
                            continue;                                                       // §4.3 exactly once
                        Arrivals.Add(new GuestArrival
                        {
                            Sign = sign,
                            Building = m.m_Target,
                            Household = hm.m_Household,
                            People = party.Length,
                            Tourist = true,
                        });
                    }
                }
            }

            /// <summary>map[key] += 1 (a dev counter). Lives in the job struct so Burst compiles it with the job.</summary>
            internal static void Bump(NativeParallelHashMap<Entity, int> map, Entity key)
            {
                map.TryGetValue(key, out int n);
                map[key] = n + 1;
            }
        }

        /// <summary>Residents: Leisure aimed at one of our targets (only lodges have a leisure provider tag),
        /// household not a tourist household; on site = CurrentBuilding is that target; on site now and not in
        /// the previous scan → one arrival (§4.4 set difference).</summary>
        [BurstCompile]
        private struct LeisureScanJob : IJob
        {
            [ReadOnly] public NativeList<ArchetypeChunk> Chunks;
            [ReadOnly] public EntityTypeHandle EntityType;
            [ReadOnly] public ComponentTypeHandle<Leisure> LeisureType;
            [ReadOnly] public ComponentTypeHandle<CurrentBuilding> CurrentBuildingType;
            [ReadOnly] public ComponentTypeHandle<HouseholdMember> HouseholdMemberType;
            [ReadOnly] public ComponentLookup<TouristHousehold> TouristHouseholds;
            [ReadOnly] public NativeParallelHashMap<Entity, Entity> TargetSign;
            [ReadOnly] public NativeParallelHashSet<Entity> OnSiteBefore;
            [ReadOnly] public NativeParallelHashSet<Entity> NewTargets;   // per-target baseline (targets new since the last scan)
            public NativeParallelHashSet<Entity> OnSiteNow;
            public NativeList<GuestArrival> Arrivals;
            public NativeParallelHashMap<Entity, int> LocalsHeading;    // dev: residents aiming at a lodge, not there yet
            public NativeArray<int> Stats;                              // [2] leisure citizens scanned, [3] residents on site
            public bool Baseline;

            public void Execute()
            {
                for (int c = 0; c < Chunks.Length; c++)
                {
                    ArchetypeChunk chunk = Chunks[c];
                    NativeArray<Entity> entities = chunk.GetNativeArray(EntityType);
                    NativeArray<Leisure> leisures = chunk.GetNativeArray(ref LeisureType);
                    NativeArray<HouseholdMember> members = chunk.GetNativeArray(ref HouseholdMemberType);
                    bool hasCb = chunk.Has(ref CurrentBuildingType);
                    NativeArray<CurrentBuilding> current = hasCb ? chunk.GetNativeArray(ref CurrentBuildingType) : default;
                    Stats[2] += entities.Length;
                    for (int i = 0; i < entities.Length; i++)
                    {
                        Entity target = leisures[i].m_TargetAgent;
                        if (target == Entity.Null || !TargetSign.TryGetValue(target, out Entity sign))
                            continue;
                        Entity household = members[i].m_Household;
                        if (TouristHouseholds.HasComponent(household))
                            continue;                                                       // §4.1 partition
                        bool onSite = hasCb && current[i].m_CurrentBuilding == target;
                        if (!onSite)
                        {
                            MeetingScanJob.Bump(LocalsHeading, sign);
                            continue;
                        }
                        Stats[3]++;
                        OnSiteNow.Add(entities[i]);
                        if (Baseline || NewTargets.Contains(target) || OnSiteBefore.Contains(entities[i]))
                            continue;                                                       // §4.4 set difference
                        Arrivals.Add(new GuestArrival
                        {
                            Sign = sign,
                            Building = target,
                            Household = household,
                            People = 1,
                            Tourist = false,
                        });
                    }
                }
            }
        }
    }
}
