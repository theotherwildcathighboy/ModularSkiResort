// SkiLiftChargeSystem — the lift running charge (Round E1, decision D23; docs/research/econ-round/spec/
// ch4-money.md §3, §10): every lift census row bills SkiMoneyRules.LiftRate per month — full
// (2,500 + 1,000 per km of line) when complete and powered, 25 % of that while unfinished (any census
// state but Complete, lone stations included: DV11), without power, or (Round M2 T2, docs/research/round-m2/
// decisions.md R3) complete and powered while its ski area has no counted run — accrued like the run upkeep and
// debited from PlayerMoney; each lift's share goes into the ledger under its ski area. Its
// ResortLiftCharge.Operating count (complete && powered: the lift still runs) is printed by the ticket gate line
// and the DUMP; since Round M2 T1 it no longer feeds the ticket gate. Stage 0 (S0) laid down the shell
// (e1-spec I-20); stream S4 wrote the system (2026-10-04). Round M2 (2026-10-10, builder B3): T2 NoOpenRun.
//
// POWER (e1-spec R1): "powered" = LiftServedRule.IsPowered(LiftCensusSystem.ReadLiftPower(base station)),
// the base station's electricity WARNING flags (the icon the player sees, with the game's 2-dispatch
// debounce, DispatchElectricitySystem.cs:126-158) — the one reader and the one predicate the score, the
// served rule and the hub use too. Read only for Complete rows (a non-complete row bills 25 % whatever its
// power, and an unfinished lift must not raise a `Lift [power] … no power` line); Powered = true (not
// read) for the others. ReadLiftPower writes the `Lift [power]` transition itself (ch2 §3.6); this system's
// `Lifts [state]` line reports the billing consequence, and since Round F7 (item E4, log-audit D13) says
// "power not read" for a row that is not Complete instead of the unread "powered".
//
// SKI AREA (ch4 §2.2, the stats system's lift rule): SkiResortIndex.TryResolve(xz,
// SkiRunOwnershipSystem.TetherMeters) at the live base station's Transform xz; with no live base, the live
// top station's; with no station, the anchor edge's start node (Game.Net.Edge.m_Start → Game.Net.Node).
// Live = exists, not Deleted, has a Transform. None within 5,000 m → Not linked (Entity.Null), which IS
// billed (the lift exists and is kept maintained, ch4 §3.4). The sign index is rebuilt on every snapshot
// from {Building, Game.Buildings.Park, PrefabRef, Transform} None {Temp, Deleted} (nothing saved: a moved
// or removed sign re-resolves at the next snapshot).
//
// SNAPSHOT (ch4 §3.3) — the published LiftCharge / ResortLiftCharge tables — is taken: on the first tick
// after a load (the anchor tick, which bills nothing), on every charge pass, when LiftCensusSystem.Revision
// moved, and when the power signature moved (LiftCensusSystem.ComputePowerSignature polled every
// Tuning.PowerPollTicks ticks = 256 sim frames, ch1's poll cadence; second pass). All of it at most once
// per Tuning.SnapshotMinFrames sim frames, so Billing, Operating and the hub follow a lift
// that closes, opens, loses or regains power within 256 frames of the game's warning (about 10 s at 1×),
// while billing keeps its 1,024-frame cadence (a rate change between passes bills from the next pass).
// Revision++ when any LiftCharge (hence any ResortLiftCharge) value changed. Round M2 T2: the area's run state
// (SkiResortAttractivenessSystem.TryGetAreaScore: released score, RunsCounted) is read on each snapshot; no new
// trigger — every charge pass snapshots right before it bills (at most SnapshotMinFrames old), so a run that
// closes or opens bills from the next pass (the transition pass bills its whole interval at the new rate; the
// carry runs on). The hub's lift row follows within one charge pass.
//
// BILLING (ch4 §3.5, the run-upkeep pattern, SkiRunUpkeepSystem): self-gated every
// Tuning.ChargeIntervalFrames elapsed sim frames (SimulationSystem.frameIndex, wrap-safe uint), accrual
// SkiMoneyRules.Accrue (carry += rate × elapsed / month; month = TimeSystem.kTicksPerDay × daysPerYear / 12,
// TimeSystem.cs:18), PlayerMoney.Subtract on the City entity (PlayerMoney.cs:38-41, clamped ±2e9).
// Attribution: each ski area's exact AccruedShare of the pass into the ledger (they sum to what accrued,
// not to the floored debit; the difference is the carry, < 1 credit). Nothing owed for the time before a
// load. A separate carry from the run upkeep's. Not in the Economy panel (closed enums, BudgetUISystem.cs
// :61-66): the hub says so. Reduced cases (SkiMoneyRules.LiftBillingOf, precedence): Unfinished, NoPower, then
// NoOpenRun (Round M2 T2: SkiMoneyRules.LiftAreaRunOpen false = linked, the area's score released and 0 runs
// counted; not linked, no score yet or the first score after a load still held bill full, as before).
//
// PHASE: GameSimulation, anchored UpdateAfter<SkiLiftChargeSystem, SkiRunUpkeepSystem> (Mod.cs; not
// load-bearing for correctness: it keeps the two monthly debits next to each other in the frame and the
// log). Pause stops the meter; the Editor never arms it.
// CADENCE: GetUpdateInterval 4 = the upkeep's slot (anchored children with equal intervals inherit the
// anchor's offset, UpdateSystem.cs:400-431); the real cadence is self-gated (above).
// COST (ch4 §8; estimates, UNVERIFIED until the field): between passes a frame-index compare, an int
// compare of the census revision and a tick counter (nothing per frame beyond that); a snapshot is
// O(lift rows + Park buildings) plus one ElectricityConsumer read per Complete row (it waits for the
// dispatch job if one is in flight — a sync point once per snapshot / poll, never per frame); a power poll
// is one consumer read per Complete row every 256 frames. A snapshot is also a Transform sync point: the
// sign index (m_SignQuery.CompleteDependency) and TryLiveXz (EntityManager reads of the stations'
// Transform) complete the move systems' Transform writers (CarMoveSystem, HumanMoveSystem …) — once per
// snapshot, i.e. per charge pass (1,024 frames) plus census / power changes, never per frame; the
// positions are needed (a station or sign moved by Move It re-resolves), so the sync stays (E1 fixer F17,
// e1-spec §8.9). Measured once per load: `Lifts [cost]` after Tuning.CostPasses charge passes — since Round
// F7 (item E3, log-audit D5) only passes whose snapshot had at least one lift row, and the power poll's mean
// over polls with a lift row (the DUMP's poll count still counts every poll).
// LOGS (ch4 §3.7, exact): created; ARMED; `Lifts [charge]` when the rounded rate changes, else every
// Tuning.LogEveryNthCharge-th pass, through a LogWindow exactly as SkiRunUpkeepSystem.Charge (first
// billable pass after a load or an idle spell forced); `Lifts [state]` — the first snapshot of a load
// lists every lift plain (at most Tuning.StateLinesPerPass + "and N more"), later changes go through
// m_StateLog (LogWindow 10 s; R15 flood fix: a brownout can flip a base station's warning every 384
// frames); `Lifts [idle]` per reason transition; the no-PlayerMoney WARN once per load; `Lifts [cost]`
// once; the one-shot `!!!` (disabled for the load). Folds are flushed on the system's own tick
// (TrailingDue), before an idle line, in OnGamePreload and in OnDestroy (LogWindow.cs header).
// CONTRACT (house pattern): "created" line; per-load reset in OnGameLoadingComplete (Game mode only);
// OnGamePreload flushes folded logs (try/caught: GameSystemBase.cs:85-96); one-shot "!!!" and disabled for
// the load (the tables are cleared: nothing is billed, nothing operates); OnDestroy flushes.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Buildings;
using Game.City;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiLiftChargeSystem : GameSystemBase
    {
        // ── Tuning — the single block (the rates are SkiMoneyRules') ───────────────────────────────────
        private static class Tuning
        {
            /// <summary>Charge pass cadence in elapsed sim frames (the upkeep's).</summary>
            public const int ChargeIntervalFrames = 1024;
            /// <summary>Heartbeat: a charge line every Nth pass even at an unchanged rate.</summary>
            public const int LogEveryNthCharge = 32;
            /// <summary>At most one charge line and one state line per this many wall-clock seconds (R15).</summary>
            public const float LogWindowS = 10f;
            /// <summary>Power-signature poll every this many ticks (64 × 4 = 256 sim frames; second pass).</summary>
            public const int PowerPollTicks = 64;
            /// <summary>At most one snapshot per this many sim frames.</summary>
            public const int SnapshotMinFrames = 64;
            /// <summary>Lifts listed plain by the first snapshot of a load.</summary>
            public const int StateLinesPerPass = 6;
            /// <summary>Charge passes with at least one lift row timed for the `Lifts [cost]` line (F7 E3).</summary>
            public const int CostPasses = 32;
        }

        /// <summary>One census lift's charge (row id / top; Sign Null = Not linked).</summary>
        internal struct LiftCharge
        {
            public Entity Id, Top, Sign;
            public LiftCensus.State State;
            public bool Powered;                                         // IsPowered(ReadLiftPower(base)) for Complete rows, else true (not read)
            public SkiMoneyRules.LiftBilling Billing;
            public float LengthM, RatePerMonth;
        }

        /// <summary>One ski area's lift totals (Entity.Null = Not linked).</summary>
        internal struct ResortLiftCharge
        {
            public float RatePerMonth;
            public int Lifts, Complete, Operating, Full, Reduced;
            public float FullKm, ReducedKm;
            public int NoOpenRun;   // Round M2 T2: of Reduced, the complete, powered lifts billed 25 % for no counted run
        }

        private struct LiftKey : IEquatable<LiftKey>
        {
            public Entity Id, Top;
            public LiftKey(Entity id, Entity top) { Id = id; Top = top; }
            public bool Equals(LiftKey o) => Id == o.Id && Top == o.Top;
            public override bool Equals(object obj) => obj is LiftKey k && Equals(k);
            public override int GetHashCode() => (Id.GetHashCode() * 397) ^ Top.GetHashCode();
        }

        private const string IdleNotArmed = "not armed (not in a city)";
        private const string IdleFirstPass = "first charge pass pending";
        private const string IdleNoLifts = "no lifts";
        private const string IdleCensusOff = "lift census not running";
        private const string IdleDisabled = "disabled after an exception (see the !!! line)";
        private const string WhyLoad = "load";
        private const string WhyPass = "charge pass";
        private const string WhyCensus = "lift census changed";
        private const string WhyPower = "lift power changed";

        // TimeSystem.kTicksPerDay (Game.Simulation/TimeSystem.cs:18) copied into a static field so the arm-time
        // check against SkiMoneyRules.TicksPerDay is not a constant expression (no unreachable-code warning).
        private static readonly int s_GameTicksPerDay = TimeSystem.kTicksPerDay;

        /// <summary>The monthly rate of the last charge pass; 0 while idle.</summary>
        public float LastMonthlyRate { get; private set; }

        /// <summary>"" while billing; else why nothing is billed.</summary>
        public string IdleReason { get; private set; } = IdleNotArmed;

        /// <summary>++ when any LiftCharge / ResortLiftCharge changed.</summary>
        internal int Revision { get; private set; }

        /// <summary>The charge of census row (<paramref name="id"/>, <paramref name="top"/>).</summary>
        internal bool TryGetLift(Entity id, Entity top, out LiftCharge charge)
        {
            return m_Lifts.TryGetValue(new LiftKey(id, top), out charge);
        }

        /// <summary>The lift totals of <paramref name="signOrNull"/> (Entity.Null = Not linked).</summary>
        internal bool TryGetResort(Entity signOrNull, out ResortLiftCharge charge)
        {
            return m_Resorts.TryGetValue(signOrNull, out charge);
        }

        // ── systems and queries ───────────────────────────────────────────────────────────────────
        private SimulationSystem m_SimulationSystem;
        private CitySystem m_CitySystem;
        private TimeSystem m_TimeSystem;
        private LiftCensusSystem m_Census;
        private SkiRunOwnershipSystem m_Ownership;
        private SkiGuestCounterSystem m_Counter;
        private SkiResortAttractivenessSystem m_Attractiveness;   // Round M2 T2: the area's run state per snapshot
        private Game.UI.NameSystem m_NameSystem;
        private EntityQuery m_SignQuery;

        // ── the published snapshot (and its next buffers, swapped) ────────────────────────────────
        private Dictionary<LiftKey, LiftCharge> m_Lifts = new Dictionary<LiftKey, LiftCharge>();
        private Dictionary<LiftKey, LiftCharge> m_LiftsNext = new Dictionary<LiftKey, LiftCharge>();
        private List<LiftCharge> m_LiftList = new List<LiftCharge>();          // census row order (logs, DUMP)
        private List<LiftCharge> m_LiftListNext = new List<LiftCharge>();
        private List<Entity> m_LiftBases = new List<Entity>();                 // the row's base station, aligned with m_LiftList
        private List<Entity> m_LiftBasesNext = new List<Entity>();
        private Dictionary<Entity, ResortLiftCharge> m_Resorts = new Dictionary<Entity, ResortLiftCharge>();
        private Dictionary<Entity, ResortLiftCharge> m_ResortsNext = new Dictionary<Entity, ResortLiftCharge>();
        private readonly List<Entity> m_ResortOrder = new List<Entity>();        // signs by Index / Version, Not linked last
        private readonly List<LiftCensus.Row> m_Rows = new List<LiftCensus.Row>();
        private readonly SkiResortIndex m_Index = new SkiResortIndex();
        private float m_TotalRate, m_TotalFullKm, m_TotalReducedKm;
        private int m_TotalFull, m_TotalReduced;
        private int m_TotalNoOpenRun;   // Round M2 T2: of m_TotalReduced, billed 25 % for no counted run

        // ── per-load state — reset in OnGameLoadingComplete ───────────────────────────────────────
        private bool m_Armed, m_Disabled, m_ErrorLogged, m_CityWarned;
        private bool m_ChargeFrameValid;
        private uint m_LastChargeFrame, m_LastSnapshotFrame;
        private float m_CarryFraction;
        private long m_ChargeCounter;
        private int m_LastLoggedMonthlyRate;
        private string m_LastIdleReason;
        private string m_SnapshotDue;
        private string m_LastSnapshotWhy = "-";
        private int m_SnapshotCensusRevision, m_SnapshotPowerSignature;
        private bool m_ListedThisLoad;
        private int m_PollTicks, m_Snapshots, m_PowerSnapshots, m_Polls;
        private int m_CostPolls;                 // F7 E3: polls timed for `Lifts [cost]` (the last snapshot had a lift row)
        private double m_PollSumMs;              // F7 E3: summed over those polls only
        private int m_CostPasses;
        private double m_CostSumMs, m_CostMaxMs;
        private bool m_CostLogged;

        // R15 / F6 D3: the charge lines and the per-lift state lines are folded separately.
        private readonly LogWindow m_ChargeLog = new LogWindow(Tuning.LogWindowS);
        private readonly LogWindow m_StateLog = new LogWindow(Tuning.LogWindowS);

        protected override void OnCreate()
        {
            base.OnCreate();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_CitySystem = World.GetOrCreateSystemManaged<CitySystem>();
            m_TimeSystem = World.GetOrCreateSystemManaged<TimeSystem>();
            m_Census = World.GetOrCreateSystemManaged<LiftCensusSystem>();
            m_Ownership = World.GetOrCreateSystemManaged<SkiRunOwnershipSystem>();
            m_Counter = World.GetOrCreateSystemManaged<SkiGuestCounterSystem>();
            // Round M2 T2: no cycle (the attractiveness system reads the lift census, never this system).
            m_Attractiveness = World.GetOrCreateSystemManaged<SkiResortAttractivenessSystem>();
            m_NameSystem = World.GetOrCreateSystemManaged<Game.UI.NameSystem>();
            m_SignQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Building>(),
                    ComponentType.ReadOnly<Game.Buildings.Park>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<Game.Objects.Transform>(),
                },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
            Mod.Log.Info($"{nameof(SkiLiftChargeSystem)} created");
        }

        /// <summary>The run upkeep's 4-frame slot (header CADENCE).</summary>
        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return 4;
        }

        /// <summary>A load (or leaving to the menu) writes the folded lines of the city being left. Never
        /// throws: GameSystemBase.GamePreload DISABLES a system whose OnGamePreload throws
        /// (Game/GameSystemBase.cs:85-96).</summary>
        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            try
            {
                FlushFolds(UnityEngine.Time.realtimeSinceStartup, all: true);
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"Lifts [charge]: folded lines could not be written before the load " +
                             $"({e.GetType().Name}: {e.Message}) — non-fatal");
            }
        }

        /// <summary>Quit to desktop raises no onGamePreload; the folds are written here (LogWindow.cs header).
        /// Teardown never throws.</summary>
        protected override void OnDestroy()
        {
            try
            {
                FlushFolds(UnityEngine.Time.realtimeSinceStartup, all: true);
            }
            catch (Exception)
            {
                // nothing left to report to
            }
            base.OnDestroy();
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            m_Armed = false;
            m_Disabled = false;
            m_ErrorLogged = false;
            m_CityWarned = false;
            m_ChargeFrameValid = false;
            m_LastChargeFrame = m_LastSnapshotFrame = 0;
            m_CarryFraction = 0f;
            m_ChargeCounter = 0;
            m_LastLoggedMonthlyRate = -1;
            m_LastIdleReason = null;
            m_SnapshotDue = null;
            m_SnapshotCensusRevision = 0;
            m_SnapshotPowerSignature = 0;
            m_ListedThisLoad = false;
            m_PollTicks = m_Snapshots = m_PowerSnapshots = m_Polls = 0;
            m_CostPolls = 0;
            m_PollSumMs = 0.0;
            m_CostPasses = 0;
            m_CostSumMs = m_CostMaxMs = 0.0;
            m_CostLogged = false;
            m_ChargeLog.Reset();   // flushed in OnGamePreload; nothing carries over
            m_StateLog.Reset();
            ClearSnapshot();
            m_LastSnapshotWhy = "-";
            Revision++;   // the tables were emptied (readers compare for equality)
            LastMonthlyRate = 0f;
            IdleReason = IdleNotArmed;

            if (mode != GameMode.Game)
                return;

            m_Armed = true;
            IdleReason = IdleFirstPass;
            if (s_GameTicksPerDay != SkiMoneyRules.TicksPerDay)
                Mod.Log.Warn($"Lifts [charge]: TimeSystem.kTicksPerDay is {s_GameTicksPerDay}, SkiMoneyRules.TicksPerDay " +
                             $"{SkiMoneyRules.TicksPerDay} — the month length of the lift charge and the ledger is off (rebuild the mod)");
            Mod.Log.Info($"Lifts [charge]: ARMED — per lift per month {SkiMoneyRules.LiftFixedPerMonth:F0} + " +
                         $"{SkiMoneyRules.LiftPerKmPerMonth:F0}/km of line (complete and powered), " +
                         $"{SkiMoneyRules.LiftReducedShare * 100f:F0} % while unfinished, without power, or while its ski area " +
                         "has no counted run (kept maintained; full until the area's first score after a load is released); " +
                         "lift = a lift census row (lone stations included); ski area = nearest Welcome Sign within " +
                         $"{SkiRunOwnershipSystem.TetherMeters:F0} m of the base station (else top station, else first node); " +
                         $"charge pass every {Tuning.ChargeIntervalFrames} sim frames, elapsed-frame accrual with fractional " +
                         $"carry; charge lines at most one per LogWindowS={Tuning.LogWindowS:0} s wall clock (a burst is " +
                         "folded into one summary)");
        }

        protected override void OnUpdate()
        {
            try
            {
                if (m_Disabled || !m_Armed)
                    return;

                uint frame = m_SimulationSystem.frameIndex;
                if (!m_ChargeFrameValid)
                {
                    // First tick after load anchors the meter (nothing owed for the pre-load past) and takes
                    // the first snapshot, so the gate and the hub have lift numbers at once.
                    m_ChargeFrameValid = true;
                    m_LastChargeFrame = frame;
                    m_PollTicks = 0;
                    Snapshot(frame, WhyLoad);
                    return;
                }

                uint elapsed = frame - m_LastChargeFrame;   // wrap-safe
                if (elapsed >= (uint)Tuning.ChargeIntervalFrames)
                {
                    m_LastChargeFrame = frame;
                    long t0 = Stopwatch.GetTimestamp();
                    if (frame - m_LastSnapshotFrame >= (uint)Tuning.SnapshotMinFrames)
                        Snapshot(frame, WhyPass);
                    Charge(elapsed);
                    RecordPassCost((Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency);
                }
                else
                {
                    // Between passes: the census revision (an int compare) and the power poll (every 256 frames).
                    if (++m_PollTicks >= Tuning.PowerPollTicks)
                    {
                        m_PollTicks = 0;
                        if (m_Census.IsArmed)
                        {
                            long t0 = Stopwatch.GetTimestamp();
                            int signature = m_Census.ComputePowerSignature();
                            if (m_Rows.Count > 0)
                            {
                                // F7 E3: the cost line times only polls with a lift to read.
                                m_PollSumMs += (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                                m_CostPolls++;
                            }
                            m_Polls++;
                            if (signature != m_SnapshotPowerSignature && m_SnapshotDue == null)
                                m_SnapshotDue = WhyPower;
                        }
                    }
                    if (m_SnapshotDue == null && m_Census.Revision != m_SnapshotCensusRevision)
                        m_SnapshotDue = WhyCensus;
                    if (m_SnapshotDue != null && frame - m_LastSnapshotFrame >= (uint)Tuning.SnapshotMinFrames)
                    {
                        if (ReferenceEquals(m_SnapshotDue, WhyPower))
                            m_PowerSnapshots++;
                        Snapshot(frame, m_SnapshotDue);
                    }
                }

                if (m_StateLog.Count > 0 || m_ChargeLog.Count > 0)
                    FlushFolds(UnityEngine.Time.realtimeSinceStartup, all: false);
            }
            catch (Exception e)
            {
                m_Disabled = true;
                LastMonthlyRate = 0f;
                IdleReason = IdleDisabled;
                ClearSnapshot();
                Revision++;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiLiftChargeSystem)} EXCEPTION — lift charge disabled until the next game " +
                                  $"load. Please report this stack:\n{e}");
                }
            }
        }

        // ── snapshot (ch4 §3.3) ───────────────────────────────────────────────────────────────────

        private void Snapshot(uint frame, string why)
        {
            m_LastSnapshotFrame = frame;
            m_LastSnapshotWhy = why;
            m_SnapshotDue = null;
            m_Snapshots++;
            m_SnapshotCensusRevision = m_Census.Revision;

            m_LiftsNext.Clear();
            m_LiftListNext.Clear();
            m_LiftBasesNext.Clear();
            m_ResortsNext.Clear();
            float total = 0f, fullKm = 0f, reducedKm = 0f;
            int full = 0, reduced = 0, noOpenRun = 0;

            bool censusOn = m_Census.IsArmed;
            if (censusOn)
            {
                m_Census.CopyRows(m_Rows);
                BuildSignIndex();
            }
            else
            {
                m_Rows.Clear();
            }

            for (int k = 0; k < m_Rows.Count; k++)
            {
                LiftCensus.Row r = m_Rows[k];
                Entity id = LiftCensusSystem.Ent(r.Id), top = LiftCensusSystem.Ent(r.Top), baseStation = LiftCensusSystem.Ent(r.Base);
                Entity sign = Resolve(r);
                bool complete = r.State == LiftCensus.State.Complete;
                bool powered = !complete || IsPowered(baseStation);   // read only for Complete rows (header POWER)
                // Round M2 T2 (R3): the area's run state, read only where it can matter (complete and powered; the
                // rule ignores it otherwise, so the short cut changes no result).
                bool areaOpen = !complete || !powered || AreaRunOpen(sign);
                SkiMoneyRules.LiftBilling billing = SkiMoneyRules.LiftBillingOf(complete, powered, areaOpen);
                float rate = SkiMoneyRules.LiftRate(billing, r.LengthM);
                float km = math.isfinite(r.LengthM) && r.LengthM > 0f ? r.LengthM / 1000f : 0f;
                var lc = new LiftCharge
                {
                    Id = id,
                    Top = top,
                    Sign = sign,
                    State = r.State,
                    Powered = powered,
                    Billing = billing,
                    LengthM = r.LengthM,
                    RatePerMonth = rate,
                };
                m_LiftsNext[new LiftKey(id, top)] = lc;
                m_LiftListNext.Add(lc);
                m_LiftBasesNext.Add(baseStation);

                m_ResortsNext.TryGetValue(sign, out ResortLiftCharge rc);
                rc.RatePerMonth += rate;
                rc.Lifts++;
                if (complete)
                    rc.Complete++;
                if (SkiMoneyRules.LiftOperating(complete, powered))
                    rc.Operating++;
                if (billing == SkiMoneyRules.LiftBilling.Full)
                {
                    rc.Full++;
                    rc.FullKm += km;
                    full++;
                    fullKm += km;
                }
                else
                {
                    rc.Reduced++;
                    rc.ReducedKm += km;
                    reduced++;
                    reducedKm += km;
                    if (billing == SkiMoneyRules.LiftBilling.NoOpenRun)
                    {
                        rc.NoOpenRun++;
                        noOpenRun++;
                    }
                }
                m_ResortsNext[sign] = rc;
                total += rate;
            }
            m_SnapshotPowerSignature = censusOn ? m_Census.ComputePowerSignature() : 0;

            bool changed = m_LiftsNext.Count != m_Lifts.Count;
            if (!changed)
            {
                foreach (var pair in m_LiftsNext)
                {
                    if (!m_Lifts.TryGetValue(pair.Key, out LiftCharge old) || !Same(old, pair.Value))
                    {
                        changed = true;
                        break;
                    }
                }
            }

            LogTransitions();

            Swap(ref m_Lifts, ref m_LiftsNext);
            Swap(ref m_LiftList, ref m_LiftListNext);
            Swap(ref m_LiftBases, ref m_LiftBasesNext);
            Swap(ref m_Resorts, ref m_ResortsNext);
            m_TotalRate = total;
            m_TotalFull = full;
            m_TotalReduced = reduced;
            m_TotalNoOpenRun = noOpenRun;
            m_TotalFullKm = fullKm;
            m_TotalReducedKm = reducedKm;
            FillResortOrder();
            if (changed)
                Revision++;
        }

        /// <summary>ch4 §3.7: the first snapshot of a load lists every lift plain (at most
        /// Tuning.StateLinesPerPass); later snapshots offer each changed lift to m_StateLog. Compares the next
        /// table (m_LiftListNext) with the published one (m_Lifts). A lift that left the census writes nothing
        /// here (the census logs it; the charge line shows the new rate).</summary>
        private void LogTransitions()
        {
            if (!m_ListedThisLoad)
            {
                if (m_LiftListNext.Count == 0 && !m_Census.IsArmed)
                    return;   // list the lifts once the census answers
                m_ListedThisLoad = true;
                int shown = Math.Min(Tuning.StateLinesPerPass, m_LiftListNext.Count);
                for (int i = 0; i < shown; i++)
                    Mod.Log.Info("Lifts [state] " + StateBody(m_LiftListNext[i], m_LiftBasesNext[i]));
                if (m_LiftListNext.Count > shown)
                    Mod.Log.Info($"Lifts [state] … and {m_LiftListNext.Count - shown} more lift change(s) this pass (see the DUMP)");
                return;
            }

            float now = float.NaN;
            for (int i = 0; i < m_LiftListNext.Count; i++)
            {
                LiftCharge next = m_LiftListNext[i];
                if (m_Lifts.TryGetValue(new LiftKey(next.Id, next.Top), out LiftCharge old) &&
                    old.State == next.State && old.Powered == next.Powered && old.Billing == next.Billing &&
                    old.Sign == next.Sign && math.round(old.RatePerMonth) == math.round(next.RatePerMonth))
                    continue;
                if (float.IsNaN(now))
                    now = UnityEngine.Time.realtimeSinceStartup;
                string body = StateBody(next, m_LiftBasesNext[i]);
                switch (m_StateLog.Offer(now, false, body))
                {
                    case LogWindow.Verdict.Plain:
                        Mod.Log.Info("Lifts [state] " + body);
                        break;
                    case LogWindow.Verdict.Summary:
                        WriteStateSummary(now);
                        break;
                }
            }
        }

        private string StateBody(LiftCharge c, Entity baseStation)
        {
            string state;
            switch (c.State)
            {
                case LiftCensus.State.Complete: state = "complete"; break;
                case LiftCensus.State.Open: state = "open"; break;
                case LiftCensus.State.Unfinished: state = "unfinished"; break;
                default: state = "stations only"; break;
            }
            string billed;
            switch (c.Billing)
            {
                case SkiMoneyRules.LiftBilling.Full: billed = "full"; break;
                case SkiMoneyRules.LiftBilling.NoPower: billed = "at 25 %: no power"; break;
                // Round M2 T2: without this case the default would print "unfinished".
                case SkiMoneyRules.LiftBilling.NoOpenRun: billed = "at 25 %: no open run in its ski area"; break;
                default: billed = "at 25 %: unfinished"; break;
            }
            // F7 E4 (header POWER): power is read only for Complete rows; the others carry Powered = true
            // unread, so they say so (field test 7: a lone, unpowered base station logged "powered").
            string power = c.State != LiftCensus.State.Complete ? "power not read" : c.Powered ? "powered" : "no power";
            return $"lift base {Fmt(baseStation)} top {Fmt(c.Top)} ({LiftName(baseStation, c.Top)}) → ski area " +
                   $"{(c.Sign == Entity.Null ? "not linked" : Fmt(c.Sign))}: {state}, {power}, " +
                   $"{c.LengthM:F0} m — billed {billed} {c.RatePerMonth:F2}/month";
        }

        private void WriteStateSummary(float now)
        {
            Mod.Log.Info($"Lifts [state] {m_StateLog.Count} more lift change(s) in {m_StateLog.SpanS(now):F0} s, folded " +
                         $"(one line per LogWindowS={Tuning.LogWindowS:0} s) — last: {m_StateLog.LastText}");
            m_StateLog.Wrote(now);
        }

        private static bool Same(LiftCharge a, LiftCharge b)
        {
            return a.Id == b.Id && a.Top == b.Top && a.Sign == b.Sign && a.State == b.State && a.Powered == b.Powered &&
                   a.Billing == b.Billing && a.LengthM.Equals(b.LengthM) && a.RatePerMonth.Equals(b.RatePerMonth);
        }

        private static void Swap<T>(ref T a, ref T b)
        {
            T t = a;
            a = b;
            b = t;
        }

        private void ClearSnapshot()
        {
            m_Lifts.Clear();
            m_LiftsNext.Clear();
            m_LiftList.Clear();
            m_LiftListNext.Clear();
            m_LiftBases.Clear();
            m_LiftBasesNext.Clear();
            m_Resorts.Clear();
            m_ResortsNext.Clear();
            m_ResortOrder.Clear();
            m_Rows.Clear();
            m_TotalRate = m_TotalFullKm = m_TotalReducedKm = 0f;
            m_TotalFull = m_TotalReduced = m_TotalNoOpenRun = 0;
        }

        private void FillResortOrder()
        {
            m_ResortOrder.Clear();
            bool notLinked = false;
            foreach (var pair in m_Resorts)
            {
                if (pair.Key == Entity.Null)
                    notLinked = true;
                else
                    m_ResortOrder.Add(pair.Key);
            }
            m_ResortOrder.Sort(EntityOrder.Instance);
            if (notLinked)
                m_ResortOrder.Add(Entity.Null);
        }

        /// <summary>Index, then Version (the SkiResortIndex / EntityRef.CompareTo order). One instance, no
        /// allocation per sort.</summary>
        private sealed class EntityOrder : IComparer<Entity>
        {
            public static readonly EntityOrder Instance = new EntityOrder();

            public int Compare(Entity a, Entity b)
            {
                return a.Index != b.Index ? a.Index.CompareTo(b.Index) : a.Version.CompareTo(b.Version);
            }
        }

        /// <summary>e1-spec R1: a live base station's power through the one reader and the one predicate.
        /// A missing or dead base station reads false (only matters for Complete rows, which have a live base).</summary>
        private bool IsPowered(Entity baseStation)
        {
            if (baseStation == Entity.Null || !EntityManager.Exists(baseStation) || EntityManager.HasComponent<Deleted>(baseStation))
                return false;
            return LiftServedRule.IsPowered(m_Census.ReadLiftPower(baseStation));
        }

        /// <summary>Round M2 T2 (decisions.md R3, thaw-rules.md item 7): the lift's ski area has a counted run, for
        /// billing — the released score's RunsCounted from the attraction snapshot (a plain main-thread read, as the
        /// ticket gate's). Unknown reads true (full): not linked, no score yet, or the area's first score after a load
        /// still held (SkiMoneyRules.LiftAreaRunOpen).</summary>
        private bool AreaRunOpen(Entity sign)
        {
            SkiResortAttractivenessSystem.AreaScoreView v = default;
            bool linked = sign != Entity.Null;
            bool known = linked && m_Attractiveness.TryGetAreaScore(sign, out v);
            return SkiMoneyRules.LiftAreaRunOpen(linked, known, known && v.WriteHeld, known ? v.Result.RunsCounted : 0);
        }

        /// <summary>ch4 §2.2 lift rule (header SKI AREA).</summary>
        private Entity Resolve(LiftCensus.Row r)
        {
            if (!TryLiveXz(LiftCensusSystem.Ent(r.Base), out float2 at) && !TryLiveXz(LiftCensusSystem.Ent(r.Top), out at))
                at = AnchorEdgeStart(LiftCensusSystem.Ent(r.Id));
            return m_Index.TryResolve(at, SkiRunOwnershipSystem.TetherMeters, out EntityRef sign, out float _)
                ? LiftCensusSystem.Ent(sign)
                : Entity.Null;
        }

        private bool TryLiveXz(Entity e, out float2 xz)
        {
            xz = new float2(float.NaN, float.NaN);
            if (e == Entity.Null || !EntityManager.Exists(e) || EntityManager.HasComponent<Deleted>(e) ||
                !EntityManager.TryGetComponent(e, out Game.Objects.Transform t))
                return false;
            xz = t.m_Position.xz;
            return true;
        }

        /// <summary>A station-less row's anchor: its id edge's start node; NaN when unreadable (TryResolve then
        /// returns Not linked). The stats system's AnchorEdgeStart rule.</summary>
        private float2 AnchorEdgeStart(Entity edge)
        {
            if (edge != Entity.Null && EntityManager.TryGetComponent(edge, out Game.Net.Edge e) &&
                EntityManager.TryGetComponent(e.m_Start, out Game.Net.Node n))
                return n.m_Position.xz;
            return new float2(float.NaN, float.NaN);
        }

        /// <summary>Live signs (Destroyed included) into the index. Completes the query's writers first
        /// (chunk reads do not wait on running jobs).</summary>
        private void BuildSignIndex()
        {
            m_Index.Clear();
            if (!m_Ownership.PrefabSetsReady || m_SignQuery.IsEmptyIgnoreFilter)
                return;
            m_SignQuery.CompleteDependency();
            EntityTypeHandle entityHandle = GetEntityTypeHandle();
            ComponentTypeHandle<PrefabRef> prefabHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
            ComponentTypeHandle<Game.Objects.Transform> transformHandle = GetComponentTypeHandle<Game.Objects.Transform>(isReadOnly: true);
            using (NativeArray<ArchetypeChunk> chunks = m_SignQuery.ToArchetypeChunkArray(Allocator.Temp))
            {
                for (int c = 0; c < chunks.Length; c++)
                {
                    NativeArray<Entity> entities = chunks[c].GetNativeArray(entityHandle);
                    NativeArray<PrefabRef> prefabs = chunks[c].GetNativeArray(ref prefabHandle);
                    NativeArray<Game.Objects.Transform> transforms = chunks[c].GetNativeArray(ref transformHandle);
                    for (int i = 0; i < entities.Length; i++)
                    {
                        if (m_Ownership.IsGatePrefab(prefabs[i].m_Prefab))
                            m_Index.Add(LiftCensusSystem.Ref(entities[i]), transforms[i].m_Position.xz);
                    }
                }
            }
        }

        // ── billing (ch4 §3.5) ────────────────────────────────────────────────────────────────────

        private void Charge(uint elapsedFrames)
        {
            if (!m_Census.IsArmed)
            {
                SetIdle(IdleCensusOff);
                return;
            }
            float total = m_TotalRate;
            if (m_LiftList.Count == 0 || !(total > 0f))
            {
                SetIdle(IdleNoLifts);
                return;
            }

            int dpy = m_TimeSystem.daysPerYear;
            LastMonthlyRate = total;
            int debit = SkiMoneyRules.Accrue(ref m_CarryFraction, total, elapsedFrames, dpy);

            // Attribution: exact shares of what accrued this pass (sum = the accrued amount; the floored debit
            // differs by the carry, < 1 credit).
            SkiResortLedger ledger = m_Counter.Ledger;
            for (int i = 0; i < m_ResortOrder.Count; i++)
            {
                Entity sign = m_ResortOrder[i];
                if (m_Resorts.TryGetValue(sign, out ResortLiftCharge rc))
                    ledger.AddLiftCharge(LiftCensusSystem.Ref(sign), SkiMoneyRules.AccruedShare(rc.RatePerMonth, elapsedFrames, dpy));
            }

            if (debit > 0)
            {
                Entity city = m_CitySystem.City;
                if (city == Entity.Null || !EntityManager.HasComponent<PlayerMoney>(city))
                {
                    m_CarryFraction += debit;   // the upkeep's rule: it keeps accruing, nothing is lost
                    IdleReason = "the City entity has no PlayerMoney (charges accrue but are not billed)";
                    if (!m_CityWarned)
                    {
                        m_CityWarned = true;
                        Mod.Log.Warn("Lifts [charge]: City entity has no PlayerMoney — charges accrue but cannot be billed " +
                                     "(warned once per load)");
                    }
                    return;
                }
                PlayerMoney money = EntityManager.GetComponentData<PlayerMoney>(city);
                money.Subtract(debit);
                EntityManager.SetComponentData(city, money);
            }

            m_ChargeCounter++;
            m_LastIdleReason = null;
            IdleReason = "";

            int rateBucket = (int)math.round(total);
            bool log = rateBucket != m_LastLoggedMonthlyRate || m_ChargeCounter % Tuning.LogEveryNthCharge == 1;
            if (!log)
                return;   // the trailing fold is flushed by OnUpdate
            float now = UnityEngine.Time.realtimeSinceStartup;
            // -1 = after a load or an idle spell: the billing (re)start is forced (the upkeep's F6 fixer rule).
            bool force = m_LastLoggedMonthlyRate < 0;
            m_LastLoggedMonthlyRate = rateBucket;
            Entity cityForLog = m_CitySystem.City;
            string balance = cityForLog != Entity.Null && EntityManager.HasComponent<PlayerMoney>(cityForLog)
                ? EntityManager.GetComponentData<PlayerMoney>(cityForLog).money.ToString()
                : "n/a";
            // Round M2 T2 (build-contract.md §6.8): the reduced part names how many are reduced for no open run.
            string body = $"-{debit} this pass (carry {m_CarryFraction:F2}) — rate {rateBucket}/month = {m_TotalFull} lift(s) " +
                          $"full ({m_TotalFullKm:F1} km) + {m_TotalReduced} at 25 % ({m_TotalReducedKm:F1} km; {m_TotalNoOpenRun} " +
                          $"for no open run); by ski area: {ByArea()}; balance {balance}";
            switch (m_ChargeLog.Offer(now, force, body, debit, rateBucket))
            {
                case LogWindow.Verdict.Plain:
                    Mod.Log.Info("Lifts [charge] " + body);
                    break;
                case LogWindow.Verdict.Summary:
                    WriteChargeSummary(now);
                    break;
            }
        }

        /// <summary>"{sign i:v} {rate}, …, not linked {rate}" (signs by Index / Version).</summary>
        private string ByArea()
        {
            var sb = new StringBuilder();
            float notLinked = 0f;
            for (int i = 0; i < m_ResortOrder.Count; i++)
            {
                Entity sign = m_ResortOrder[i];
                if (!m_Resorts.TryGetValue(sign, out ResortLiftCharge rc))
                    continue;
                if (sign == Entity.Null)
                {
                    notLinked = rc.RatePerMonth;
                    continue;
                }
                sb.Append(Fmt(sign)).Append(' ').Append(math.round(rc.RatePerMonth).ToString("F0")).Append(", ");
            }
            sb.Append("not linked ").Append(math.round(notLinked).ToString("F0"));
            return sb.ToString();
        }

        private void WriteChargeSummary(float now)
        {
            Mod.Log.Info($"Lifts [charge] {m_ChargeLog.Count} more charge line(s) in {m_ChargeLog.SpanS(now):F0} s, folded " +
                         $"(one line per LogWindowS={Tuning.LogWindowS:0} s): debit {m_ChargeLog.Range(0, "F0")} per pass, " +
                         $"rate {m_ChargeLog.Range(1, "F0")}/month — last: " + m_ChargeLog.LastText);
            m_ChargeLog.Wrote(now);
        }

        /// <summary>Folded lines whose window has passed (or all of them before a load / at quit).</summary>
        private void FlushFolds(float now, bool all)
        {
            if (m_ChargeLog.Count > 0 && (all || m_ChargeLog.TrailingDue(now)))
                WriteChargeSummary(now);
            if (m_StateLog.Count > 0 && (all || m_StateLog.TrailingDue(now)))
                WriteStateSummary(now);
        }

        /// <summary>Idle states log once per reason transition (the upkeep's rule).</summary>
        private void SetIdle(string reason)
        {
            LastMonthlyRate = 0f;
            IdleReason = reason;
            if (m_LastIdleReason == reason)
                return;
            m_LastIdleReason = reason;
            m_LastLoggedMonthlyRate = -1;   // next billable pass logs unconditionally
            if (m_ChargeLog.Count > 0)
                WriteChargeSummary(UnityEngine.Time.realtimeSinceStartup);
            Mod.Log.Info($"Lifts [idle]: {reason} — nothing billed; re-evaluated every {Tuning.ChargeIntervalFrames} sim frames");
        }

        /// <summary>`Lifts [cost]`, once per load. F7 E3 (header COST): only passes whose snapshot had at least
        /// one lift row count (field test 7: 32 passes over an empty census measured nothing).</summary>
        private void RecordPassCost(double ms)
        {
            if (m_CostLogged || m_Rows.Count == 0)
                return;
            m_CostPasses++;
            m_CostSumMs += ms;
            if (ms > m_CostMaxMs)
                m_CostMaxMs = ms;
            if (m_CostPasses < Tuning.CostPasses)
                return;
            m_CostLogged = true;
            Mod.Log.Info($"Lifts [cost] main thread per pass: mean {m_CostSumMs / m_CostPasses:F3} ms, max {m_CostMaxMs:F3} ms " +
                         $"over {m_CostPasses} passes with lifts ({m_Rows.Count} lift rows, {m_Index.Count} signs); power poll every " +
                         $"{Tuning.PowerPollTicks * 4} frames mean {(m_CostPolls > 0 ? m_PollSumMs / m_CostPolls : 0.0):F3} ms over " +
                         $"{m_CostPolls} poll(s) with lifts, {m_PowerSnapshots} snapshot(s) taken on a power change");
        }

        // ── names and the DUMP ────────────────────────────────────────────────────────────────────

        /// <summary>The base station's custom name, else the top's (quoted), else "lift" (NameSystem.cs:209-212).</summary>
        private string LiftName(Entity baseStation, Entity top)
        {
            if (TryCustomName(baseStation, out string name) || TryCustomName(top, out name))
                return "'" + name + "'";
            return "lift";
        }

        private bool TryCustomName(Entity e, out string name)
        {
            name = null;
            if (e == Entity.Null || !m_NameSystem.TryGetCustomName(e, out string n) || string.IsNullOrEmpty(n))
                return false;
            name = n;
            return true;
        }

        private static string Fmt(Entity e) => e == Entity.Null ? "none" : e.Index + ":" + e.Version;

        /// <summary>DUMP lines (appends only; never throws).</summary>
        internal void AppendDump(StringBuilder sb)
        {
            try
            {
                sb.Append("  Lifts [charge]: armed ").Append(m_Armed).Append(", disabled ").Append(m_Disabled)
                  .Append(", idle '").Append(IdleReason).Append("', rate ").Append(m_TotalRate.ToString("F2"))
                  .Append("/month (last pass ").Append(LastMonthlyRate.ToString("F2")).Append("), carry ")
                  .Append(m_CarryFraction.ToString("F2")).Append(", passes ").Append(m_ChargeCounter)
                  .Append(", snapshots ").Append(m_Snapshots).Append(" (").Append(m_PowerSnapshots)
                  .Append(" on a power change; last '").Append(m_LastSnapshotWhy).Append("', due ")
                  .Append(m_SnapshotDue ?? "none").Append("), census revision ")
                  .Append(m_SnapshotCensusRevision).Append(", power signature ").Append(m_SnapshotPowerSignature)
                  .Append(", power polls ").Append(m_Polls).Append(", revision ").Append(Revision)
                  .Append(", lifts ").Append(m_LiftList.Count).Append(" (").Append(m_TotalFull).Append(" full ")
                  .Append(m_TotalFullKm.ToString("F1")).Append(" km, ").Append(m_TotalReduced).Append(" at 25 % ")
                  .Append(m_TotalReducedKm.ToString("F1")).Append(" km)\n");
                for (int i = 0; i < m_ResortOrder.Count; i++)
                {
                    Entity sign = m_ResortOrder[i];
                    if (!m_Resorts.TryGetValue(sign, out ResortLiftCharge rc))
                        continue;
                    sb.Append("    ski area ").Append(sign == Entity.Null ? "not linked" : Fmt(sign)).Append(": ")
                      .Append(rc.Lifts).Append(" lift(s), ").Append(rc.Complete).Append(" complete, ")
                      .Append(rc.Operating).Append(" operating, ").Append(rc.Full).Append(" full (")
                      .Append(rc.FullKm.ToString("F1")).Append(" km) + ").Append(rc.Reduced).Append(" at 25 % (")
                      .Append(rc.ReducedKm.ToString("F1")).Append(" km; ").Append(rc.NoOpenRun).Append(" for no open run) = ")
                      .Append(rc.RatePerMonth.ToString("F2")).Append("/month\n");
                }
                for (int i = 0; i < m_LiftList.Count; i++)
                    sb.Append("    ").Append(StateBody(m_LiftList[i], m_LiftBases[i])).Append('\n');
            }
            catch (Exception e)
            {
                sb.Append("  Lifts [charge]: dump failed (").Append(e.GetType().Name).Append(": ").Append(e.Message).Append(")\n");
            }
        }
    }
}
