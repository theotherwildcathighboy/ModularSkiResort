// The hub's monthly history store, the C# half. The element and its bytes are pure (SkiResortHistory.cs); this file holds their
// ECS halves and the system that fills them. SkiResortHubUISystem publishes the `history` binding from these buffers.
// Store: a DynamicBuffer<SkiResortHistoryMonth> on each Welcome Sign, one element per closed calendar month, oldest first, saved
// by the game with no registration in Mod.cs (Colossal ComponentSerializerLibrary.cs:73-87, 1.6.2f1).
// Sampling: GameSimulation, UpdateAfter SkiGuestCounterSystem with the counter's interval (16), so it inherits the counter's offset
// and runs right after it in the same frame (UpdateSystem.cs:400-431); month roll / jump / start are told by LastMonthStep.Generation.
// The month in progress is mirrored onto the sign every Tuning.MirrorEveryFrames (256) frames, so a save is at most that far behind.
// Engine notes: EngineNotes/SkiResortHistorySystem.md

using System;
using System.Collections.Generic;
using System.Text;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    /// <summary>ECS half of the pure SkiResortHistoryMonth (SkiResortHistory.cs holds the fields, the layout and the codec).
    /// NEVER rename, move or turn into a component (header of SkiResortHistory.cs).</summary>
    // Self-alias: the stripped saved AQN, a literal; why and the rules in SkiRunBinding.cs "SELF-ALIAS". Here, not on the pure half:
    // SkiResortHistory.cs stays free of Colossal types (tests/ResortBatch links it).
    [FormerlySerializedAs("SkiResortMod.Systems.SkiResortHistoryMonth, SkiResortMod")]
    [InternalBufferCapacity(0)]
    public partial struct SkiResortHistoryMonth : IBufferElementData, ISerializable
    {
        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var w = new HistoryWriter<TWriter>(writer);
            WriteTo(ref w);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            var r = new HistoryReader<TReader>(reader);
            ReadFrom(ref r);
        }
    }

    /// <summary>ECS half of the pure SkiResortMonthInProgress. One per Welcome Sign; written only by SkiResortHistorySystem.</summary>
    // Self-alias: as SkiResortHistoryMonth above.
    [FormerlySerializedAs("SkiResortMod.Systems.SkiResortMonthInProgress, SkiResortMod")]
    public partial struct SkiResortMonthInProgress : IComponentData, IQueryTypeParameter, ISerializable
    {
        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var w = new HistoryWriter<TWriter>(writer);
            WriteTo(ref w);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            var r = new HistoryReader<TReader>(reader);
            ReadFrom(ref r);
        }
    }

    /// <summary>The pure codec's writer over Colossal's IWriter (typed locals pin each overload, as in SkiRunBinding).</summary>
    internal struct HistoryWriter<TWriter> : IHistoryWriter where TWriter : IWriter
    {
        private TWriter m_Writer;
        public HistoryWriter(TWriter writer) { m_Writer = writer; }
        public void Write(byte value) { m_Writer.Write(value); }
        public void Write(int value) { m_Writer.Write(value); }
        public void Write(long value) { m_Writer.Write(value); }
        public void Write(double value) { m_Writer.Write(value); }
    }

    internal struct HistoryReader<TReader> : IHistoryReader where TReader : IReader
    {
        private TReader m_Reader;
        public HistoryReader(TReader reader) { m_Reader = reader; }
        public void Read(out byte value) { m_Reader.Read(out value); }
        public void Read(out int value) { m_Reader.Read(out value); }
        public void Read(out long value) { m_Reader.Read(out value); }
        public void Read(out double value) { m_Reader.Read(out value); }
    }

    /// <summary>The pure ring ops over a sign's buffer.</summary>
    internal struct HistoryBufferStore : IHistoryStore
    {
        public DynamicBuffer<SkiResortHistoryMonth> Buffer;
        public HistoryBufferStore(DynamicBuffer<SkiResortHistoryMonth> buffer) { Buffer = buffer; }
        public int Count => Buffer.Length;
        public SkiResortHistoryMonth this[int i] { get => Buffer[i]; set => Buffer[i] = value; }
        public void Add(SkiResortHistoryMonth m) { Buffer.Add(m); }
        public void RemoveRange(int index, int count) { Buffer.RemoveRange(index, count); }
    }

    public sealed partial class SkiResortHistorySystem : GameSystemBase
    {
        private static class Tuning
        {
            /// <summary>The month-in-progress mirror period in sim frames (Engine notes: Month in progress).</summary>
            public const int MirrorEveryFrames = 256;
            /// <summary>A roll's snapshot (stats rebuild, SkiDaySystem's month step) is waited for at most this many sim frames.</summary>
            public const int SnapshotTimeoutFrames = 4096;
            /// <summary>Areas listed by name in the ARMED line (the rest are counted).</summary>
            public const int ArmedListMax = 8;
        }

        private struct Pending
        {
            public Entity Sign;
            public SkiResortHistoryMonth Element;
        }

        private SkiGuestCounterSystem m_Counter;
        private SkiResortStatsSystem m_Stats;
        private SkiDaySystem m_SkiDays;
        private SimulationSystem m_Simulation;
        private TimeSystem m_Time;
        private Game.UI.NameSystem m_NameSystem;
        private EntityQuery m_HistoryQuery, m_ProgressQuery;

        private bool m_Armed, m_Disabled, m_ErrorLogged;
        private int m_SeenGeneration;
        private uint m_LastMirrorFrame;
        private int m_LastMirrorRevision;
        private bool m_Mirrored;

        private readonly List<Pending> m_Pending = new List<Pending>(8);
        private bool m_HasPending;
        private int m_PendingClosedKey, m_PendingNewKey;
        private uint m_PendingSince;
        private long m_RebuildsAtRequest;

        private readonly List<Entity> m_Signs = new List<Entity>(8);           // scratch: live signs of this step
        private readonly HashSet<Entity> m_SignSet = new HashSet<Entity>();
        private readonly StringBuilder m_Sb = new StringBuilder(256);

        /// <summary>Moves whenever a stored month changes (append, replace, gap marker) and at every city load. The hub re-sends
        /// `history` when it moved.</summary>
        public int Revision { get; private set; }

        /// <summary>Moves at every month-in-progress mirror write (at most every MirrorEveryFrames sim frames, and only when the ledger
        /// moved). The hub re-sends the month in progress on it, throttled.</summary>
        public int ProgressRevision { get; private set; }

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Counter = World.GetOrCreateSystemManaged<SkiGuestCounterSystem>();
            m_Stats = World.GetOrCreateSystemManaged<SkiResortStatsSystem>();
            m_SkiDays = World.GetOrCreateSystemManaged<SkiDaySystem>();
            m_Simulation = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_Time = World.GetOrCreateSystemManaged<TimeSystem>();
            m_NameSystem = World.GetOrCreateSystemManaged<Game.UI.NameSystem>();
            m_HistoryQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<SkiResortHistoryMonth>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Game.Tools.Temp>() },
            });
            m_ProgressQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<SkiResortMonthInProgress>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Game.Tools.Temp>() },
            });
            Mod.Log.Info($"{nameof(SkiResortHistorySystem)} created");
        }

        /// <summary>The counter's interval: with UpdateAfter it inherits the counter's offset (Engine notes: Sampling).</summary>
        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return 16;
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Armed = false;
            m_Disabled = false;
            m_ErrorLogged = false;
            m_Pending.Clear();
            m_HasPending = false;
            m_Mirrored = false;
            m_LastMirrorFrame = 0;
            m_LastMirrorRevision = int.MinValue;
            Revision++;   // another city's buffers: the hub re-sends `history`
            m_SeenGeneration = m_Counter.LastMonthStep.Generation;   // the next step (Started) is this load's
            if (mode != GameMode.Game)
                return;
            m_Armed = true;
            try
            {
                Mod.Log.Info("History: ARMED — one sample per ski area per closed calendar month on its Welcome Sign (saved; no " +
                             $"range cap, {SkiResortHistory.CapMonths}-month safety cap), taken at the guest ledger's roll + one stats " +
                             $"snapshot; month in progress mirrored onto the sign every {Tuning.MirrorEveryFrames} sim frames and seeded " +
                             "back on load when its month matches; " + DescribeStored());
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"History: ARMED (the stored history could not be described: {e.GetType().Name}: {e.Message})");
            }
        }

        protected override void OnUpdate()
        {
            try
            {
                if (m_Disabled || !m_Armed || !m_Counter.IsArmed)
                    return;
                SkiGuestCounterSystem.MonthStepInfo step = m_Counter.LastMonthStep;
                bool stepped = step.Generation != m_SeenGeneration;
                if (stepped)
                {
                    m_SeenGeneration = step.Generation;
                    switch (step.Result)
                    {
                        case SkiResortLedger.AdvanceResult.Started:
                            SeedFromSave();
                            break;
                        case SkiResortLedger.AdvanceResult.Rolled:
                            BeginRoll(step);
                            break;
                        case SkiResortLedger.AdvanceResult.Jumped:
                            RecordJump(step);
                            break;
                    }
                }
                if (m_HasPending)
                    TryCommit(force: false);
                Mirror(stepped);
            }
            catch (Exception e)
            {
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiResortHistorySystem)} EXCEPTION — the hub history stops recording until the next " +
                                  $"game load (months closed meanwhile are not sampled). Please report this stack:\n{e}");
                }
            }
        }


        private void SeedFromSave()
        {
            SkiResortLedger ledger = m_Counter.Ledger;
            int key = ledger.MonthKey;
            int seeded = 0, stale = 0, total = 0;
            int staleKey = SkiResortLedger.NoMonth;
            using (NativeArray<Entity> entities = m_ProgressQuery.ToEntityArray(Allocator.Temp))
            {
                total = entities.Length;
                bool partial = false, byJump = false, any = false;
                int frames = 0;
                for (int i = 0; i < entities.Length; i++)
                {
                    SkiResortMonthInProgress p = EntityManager.GetComponentData<SkiResortMonthInProgress>(entities[i]);
                    if (!SkiResortHistory.ShouldSeed(key, p))
                        continue;
                    // All signs are mirrored together, so they share the state; take the most cautious reading.
                    any = true;
                    partial |= (p.m_Flags & SkiResortMonthInProgress.FlagPartial) != 0;
                    byJump |= (p.m_Flags & SkiResortMonthInProgress.FlagByJump) != 0;
                    frames = Math.Max(frames, p.m_FramesSimulated);
                }
                if (any && ledger.SeedMonth(key, partial))
                {
                    long upkeep = 0, fees = 0;
                    for (int i = 0; i < entities.Length; i++)
                    {
                        SkiResortMonthInProgress p = EntityManager.GetComponentData<SkiResortMonthInProgress>(entities[i]);
                        if (!SkiResortHistory.ShouldSeed(key, p))
                        {
                            stale++;
                            staleKey = p.m_MonthKey;
                            continue;
                        }
                        ledger.SeedThis(SkiResortStatsSystem.ToRef(entities[i]), p.m_Month, p.m_PassSeasonKey, p.m_PassSeasonSold,
                                        p.m_PassSeasonIncome, p.m_PassSeasonPre, p.m_PassSeasonPreIncome);
                        upkeep += Math.Max(0L, p.m_Month.RunUpkeep);
                        fees += Math.Max(0L, p.m_Month.TicketIncome) + Math.Max(0L, p.m_Month.PassIncome) +
                                Math.Max(0L, p.m_Month.LessonIncome) + Math.Max(0L, p.m_Month.LodgeSpendIncome);
                        seeded++;
                    }
                    m_Counter.SeedMonthMeasures((uint)Math.Max(0, frames), partial, byJump, upkeep, fees);
                    Mod.Log.Info($"History [load] month in progress {MonthText(key)}: seeded {seeded} ski area(s) from the save " +
                                 $"({(partial ? "still partial: counting had started partway before the save" : "whole: no \"(partial)\" label")}, " +
                                 $"{SkiMoneyRules.SimulatedPct((uint)Math.Max(0, frames), m_Time.daysPerYear)} % " +
                                 $"of the month simulated before it, run upkeep {upkeep}, resort income {fees})" +
                                 (stale > 0 ? $"; {stale} stale component(s) of {MonthText(staleKey)} ignored" : "") +
                                 "; Not linked lift charges and unbound run upkeep are not restored");
                    return;
                }
                for (int i = 0; i < entities.Length; i++)
                {
                    int k = EntityManager.GetComponentData<SkiResortMonthInProgress>(entities[i]).m_MonthKey;
                    if (k != key) { stale++; staleKey = k; }
                }
            }
            Mod.Log.Info($"History [load] month in progress {MonthText(key)}: nothing seeded — " +
                         (total == 0 ? "the save holds no month in progress (a new city, or one saved before T78)"
                                     : $"{stale} saved component(s) of {(staleKey == SkiResortLedger.NoMonth ? "no month" : MonthText(staleKey))}, not this month") +
                         "; this month counts since the load (the hub shows it \"(partial)\")");
        }


        private void BeginRoll(in SkiGuestCounterSystem.MonthStepInfo step)
        {
            if (m_HasPending)
                TryCommit(force: true);   // never reached (a month is ~1000x the timeout); kept so nothing is dropped silently
            SkiResortLedger ledger = m_Counter.Ledger;
            byte flags = (byte)((step.ClosedSinceLoad ? HistoryFlags.Partial : 0) | (step.ClosedShort ? HistoryFlags.Short : 0));
            CollectSigns();
            m_Pending.Clear();
            for (int i = 0; i < m_Signs.Count; i++)
            {
                Entity sign = m_Signs[i];
                SkiResortLedger.Month last = ledger.TryGet(SkiResortStatsSystem.ToRef(sign), out SkiResortLedger.Entry e) ? e.Last : default;
                m_Pending.Add(new Pending { Sign = sign, Element = SkiResortHistoryMonth.FromLedger(last, step.ClosedKey, flags) });
            }
            m_PendingClosedKey = step.ClosedKey;
            m_PendingNewKey = step.NewKey;
            m_PendingSince = m_Simulation.frameIndex;
            m_RebuildsAtRequest = m_Stats.Rebuilds;
            m_HasPending = true;
            if (m_Pending.Count > 0)
                m_Stats.RequestRebuild("history");
            else
                TryCommit(force: true);   // no ski area: one line, nothing stored
        }

        private void TryCommit(bool force)
        {
            uint waited = m_Simulation.frameIndex - m_PendingSince;
            bool statsDone = !m_Stats.IsArmed || m_Stats.IsDisabled || m_Stats.Rebuilds > m_RebuildsAtRequest;
            int daysKey = m_SkiDays != null && m_SkiDays.IsArmed ? m_SkiDays.MonthKey : SkiResortLedger.NoMonth;
            bool daysDone = daysKey != m_PendingClosedKey;   // stepped to the new month, unarmed, or out of step (then unknown)
            bool timedOut = waited >= Tuning.SnapshotTimeoutFrames;
            if (!force && !timedOut && !(statsDone && daysDone))
                return;
            bool statsFresh = m_Stats.Rebuilds > m_RebuildsAtRequest;
            bool daysFresh = daysKey == m_PendingNewKey;
            if (m_Pending.Count == 0)
                Mod.Log.Info($"History [month] {MonthText(m_PendingClosedKey)}: no ski area — nothing stored");
            for (int i = 0; i < m_Pending.Count; i++)
                Commit(m_Pending[i], statsFresh, daysFresh, waited);
            m_Pending.Clear();
            m_HasPending = false;
        }

        private void Commit(in Pending p, bool statsFresh, bool daysFresh, uint waited)
        {
            Entity sign = p.Sign;
            if (!EntityManager.Exists(sign) || EntityManager.HasComponent<Deleted>(sign))
            {
                Mod.Log.Info($"History [month] {MonthText(p.Element.m_MonthKey)} ski area {Fmt(sign)}: deleted before its sample was " +
                             "stored — nothing stored (its history went with it)");
                return;
            }
            SkiResortHistoryMonth m = p.Element;
            // Snapshot (stats rebuild after the roll): rates and point values at the roll.
            bool snap = false;
            if (statsFresh)
            {
                for (int i = 0; i < m_Stats.ResortLiveCount; i++)
                {
                    ResortLiveStats live = m_Stats.GetResortLive(i);
                    if (live.Sign != sign)
                        continue;
                    m.m_BuildingsCost = live.BuildingsMonth >= 0 ? live.BuildingsMonth : SkiResortHistoryMonth.Unknown;
                    m.m_WagesCost = live.WagesMonth >= 0 ? live.WagesMonth : SkiResortHistoryMonth.Unknown;
                    m.m_Attraction = live.Attraction >= 0 ? live.Attraction : SkiResortHistoryMonth.Unknown;
                    m.m_SnowPct = SkiResortHistoryMonth.SnowByte(live.SnowPct);
                    snap = true;
                    break;
                }
            }
            if (!snap)
                m.m_Flags |= HistoryFlags.SnapshotMissing;
            // Ski days of the closed month (SkiDaySystem keeps it as last month once it stepped).
            if (daysFresh && m_SkiDays.TryGetArea(sign, out SkiDayAreaView view) && view.ArrivedLastMonth >= 0)
            {
                m.m_SkiDays = view.ArrivedLastMonth;
                if (m_SkiDays.LastMonthPartial && (m.m_Flags & HistoryFlags.Partial) == 0)
                    m.m_Flags |= HistoryFlags.SkiDaysPartial;
            }
            Store(sign, m, out SkiResortHistory.AppendResult result, out int length);
            WriteMonthLine(sign, m, result, length, snap ? "" : $" (snapshot missing after {waited} frames)");
        }

        private void Store(Entity sign, SkiResortHistoryMonth m, out SkiResortHistory.AppendResult result, out int length)
        {
            if (!EntityManager.HasBuffer<SkiResortHistoryMonth>(sign))
                EntityManager.AddBuffer<SkiResortHistoryMonth>(sign);   // structural: once per sign per city
            DynamicBuffer<SkiResortHistoryMonth> buffer = EntityManager.GetBuffer<SkiResortHistoryMonth>(sign);
            var store = new HistoryBufferStore(buffer);
            if (store.Count == 0)
                m.m_Flags |= HistoryFlags.First;
            result = SkiResortHistory.Append(ref store, m);
            length = store.Count;
            if (result != SkiResortHistory.AppendResult.Rejected)
                Revision++;   // the hub re-sends `history`
        }


        private void RecordJump(in SkiGuestCounterSystem.MonthStepInfo step)
        {
            if (m_HasPending)
                TryCommit(force: true);
            int recorded = 0;
            using (NativeArray<Entity> entities = m_ProgressQuery.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    SkiResortMonthInProgress p = EntityManager.GetComponentData<SkiResortMonthInProgress>(entities[i]);
                    if (p.m_MonthKey != step.ClosedKey || step.ClosedKey == SkiResortLedger.NoMonth)
                        continue;
                    SkiResortHistoryMonth m = SkiResortHistoryMonth.FromLedger(p.m_Month, step.ClosedKey,
                                                                               (byte)(HistoryFlags.Partial | HistoryFlags.SnapshotMissing));
                    Store(entities[i], m, out SkiResortHistory.AppendResult result, out int length);
                    WriteMonthLine(entities[i], m, result, length, " (the month in progress before a calendar jump, from the mirror)");
                    recorded++;
                }
            }
            Mod.Log.Info($"History [jump] the calendar jumped from {MonthText(step.ClosedKey)} to {MonthText(step.NewKey)}: {recorded} " +
                         "ski area(s) recorded the month in progress as partial; " +
                         (step.NewKey > step.ClosedKey + 1 ? "the skipped months become a gap marker at the next roll"
                                                           : step.NewKey < step.ClosedKey ? "the calendar went BACK: rolls before " +
                                                             $"{MonthText(step.ClosedKey + 1)} are not stored (history never rewrites its past)" : "no month skipped"));
        }


        private void Mirror(bool stepped)
        {
            SkiResortLedger ledger = m_Counter.Ledger;
            int key = ledger.MonthKey;
            if (key == SkiResortLedger.NoMonth)
                return;
            uint now = m_Simulation.frameIndex;
            bool due = stepped || !m_Mirrored ||
                       (now - m_LastMirrorFrame >= Tuning.MirrorEveryFrames && ledger.Revision != m_LastMirrorRevision);
            if (!due)
                return;
            bool partial = ledger.ThisMonthSinceLoad;
            bool byJump = m_Counter.MonthByJump;
            uint frames = m_Counter.MonthFramesSimulated;
            CollectSigns();
            for (int i = 0; i < m_Signs.Count; i++)
            {
                Entity sign = m_Signs[i];
                EntityRef r = SkiResortStatsSystem.ToRef(sign);
                SkiResortLedger.Entry e = ledger.TryGet(r, out SkiResortLedger.Entry found) ? found : new SkiResortLedger.Entry { Sign = r };
                SkiResortMonthInProgress p = SkiResortMonthInProgress.From(e, key, partial, byJump, frames);
                if (EntityManager.HasComponent<SkiResortMonthInProgress>(sign))
                    EntityManager.SetComponentData(sign, p);
                else
                    EntityManager.AddComponentData(sign, p);   // structural: once per sign per city
            }
            m_LastMirrorFrame = now;
            m_LastMirrorRevision = ledger.Revision;
            m_Mirrored = true;
            ProgressRevision++;   // the hub re-sends the month in progress, throttled
        }


        /// <summary>The live signs: the counter's target-map signs ∪ the ledger's linked entries, existing and not Deleted.</summary>
        private void CollectSigns()
        {
            m_Signs.Clear();
            m_SignSet.Clear();
            IReadOnlyList<Entity> live = m_Counter.LiveSigns;
            for (int i = 0; i < live.Count; i++)
                AddSign(live[i]);
            SkiResortLedger ledger = m_Counter.Ledger;
            for (int i = 0; i < ledger.Count; i++)
            {
                EntityRef r = ledger.At(i).Sign;
                if (!r.IsNull)
                    AddSign(SkiResortStatsSystem.ToEntity(r));
            }
        }

        private void AddSign(Entity sign)
        {
            if (sign == Entity.Null || !m_SignSet.Add(sign))
                return;
            if (!EntityManager.Exists(sign) || EntityManager.HasComponent<Deleted>(sign) || EntityManager.HasComponent<Game.Tools.Temp>(sign))
                return;
            m_Signs.Add(sign);
        }

        private void WriteMonthLine(Entity sign, in SkiResortHistoryMonth m, SkiResortHistory.AppendResult result, int length, string note)
        {
            StringBuilder sb = m_Sb.Clear();
            sb.Append("History [month] ").Append(MonthText(m.m_MonthKey)).Append(" ski area ").Append(Fmt(sign)).Append(NameSuffix(sign))
              .Append(": tourists ").Append(m.m_Tourists).Append(", residents ").Append(m.m_Residents)
              .Append(", ski days ").Append(m.m_SkiDays < 0 ? "?" : m.m_SkiDays.ToString())
              .Append(", tickets paid ").Append(m.m_TicketsPaid)
              .Append("; income ticket ").Append(m.m_TicketIncome).Append(" / lodge ").Append(m.m_LodgeIncome)
              .Append(" / pass ").Append(m.m_PassIncome).Append(" / lesson ").Append(m.m_LessonIncome)
              .Append("; costs lift ").Append(m.m_LiftCost).Append(" / run ").Append(m.m_RunCost)
              .Append(" / buildings ").Append(m.m_BuildingsCost < 0 ? "?" : m.m_BuildingsCost.ToString())
              .Append(" / wages ").Append(m.m_WagesCost < 0 ? "?" : m.m_WagesCost.ToString())
              .Append("; A ").Append(m.m_Attraction < 0 ? "?" : m.m_Attraction.ToString())
              .Append(", snow ").Append(m.m_SnowPct == SkiResortHistoryMonth.SnowUnknown ? "?" : m.m_SnowPct.ToString()).Append(" %")
              .Append(", flags ").Append(FlagsText(m.m_Flags)).Append(note).Append(" → ");
            switch (result)
            {
                case SkiResortHistory.AppendResult.Appended:
                    sb.Append("stored");
                    break;
                case SkiResortHistory.AppendResult.AppendedAfterGap:
                    sb.Append("stored after a gap marker (months missing)");
                    break;
                case SkiResortHistory.AppendResult.Replaced:
                    sb.Append("replaced the sample of the same month");
                    break;
                default:
                    sb.Append("NOT stored: older than the last stored month (the calendar went back)");
                    break;
            }
            sb.Append("; ").Append(length).Append(" element(s) on the sign");
            Mod.Log.Info(sb.ToString());
        }

        private string DescribeStored()
        {
            int areas = 0;
            long elements = 0;
            StringBuilder sb = new StringBuilder(128);
            using (NativeArray<Entity> entities = m_HistoryQuery.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    DynamicBuffer<SkiResortHistoryMonth> b = EntityManager.GetBuffer<SkiResortHistoryMonth>(entities[i], isReadOnly: true);
                    areas++;
                    elements += b.Length;
                    if (areas <= Tuning.ArmedListMax)
                    {
                        sb.Append(areas == 1 ? "" : ", ").Append(Fmt(entities[i])).Append(NameSuffix(entities[i])).Append(' ').Append(b.Length);
                        if (b.Length > 0)
                            sb.Append(" (").Append(MonthText(b[0].m_MonthKey)).Append("..").Append(MonthText(b[b.Length - 1].m_MonthKey)).Append(')');
                    }
                }
                int progress = m_ProgressQuery.CalculateEntityCount();
                if (areas == 0)
                    return $"no stored history in this city yet; {progress} saved month(s) in progress";
                return $"{areas} ski area(s) hold {elements} element(s) ({elements * SkiResortHistoryMonth.V1Bytes} bytes): " + sb +
                       (areas > Tuning.ArmedListMax ? $" and {areas - Tuning.ArmedListMax} more" : "") +
                       $"; {progress} saved month(s) in progress";
            }
        }

        private static string FlagsText(byte f)
        {
            if (f == 0)
                return "none";
            var parts = new List<string>(4);
            if ((f & HistoryFlags.Partial) != 0) parts.Add("partial");
            if ((f & HistoryFlags.Short) != 0) parts.Add("short");
            if ((f & HistoryFlags.CostsEstimated) != 0) parts.Add("costs-estimated");
            if ((f & HistoryFlags.SnapshotMissing) != 0) parts.Add("snapshot-missing");
            if ((f & HistoryFlags.Gap) != 0) parts.Add("gap");
            if ((f & HistoryFlags.SkiDaysPartial) != 0) parts.Add("ski-days-partial");
            if ((f & HistoryFlags.First) != 0) parts.Add("first");
            return string.Join("|", parts);
        }

        private string NameSuffix(Entity e)
        {
            if (e == Entity.Null || !m_NameSystem.TryGetCustomName(e, out string name) || string.IsNullOrEmpty(name))
                return "";
            return " '" + name + "'";
        }

        private static string MonthText(int key)
        {
            if (key == SkiResortLedger.NoMonth)
                return "none";
            return SkiMoneyRules.YearOfKey(key) + "-" + SkiMoneyRules.MonthOfKey(key).ToString("D2");
        }

        private static string Fmt(Entity e) => e.Index + ":" + e.Version;
    }
}
