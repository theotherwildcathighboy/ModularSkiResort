// Quick runs tool: the build. Enter refuses a stale plan (re-scan) and a batch the city cannot pay. Otherwise the enabled candidates are
// built in chunks of BuildChunkRuns (smoothness over speed: no long single-frame hitch, "Building runs 4 / 11"). Order (plan 7.6): the cat
// tracks first, then the pistes, then the lifts switched on (ResortLiftBuildSystem's queue, after the last chunk). Round 2 paints the cat
// tracks as cat track areas (no grading yet). A runs-first plan has up to about 170 areas. The tool emits each chunk
// in one frame (RunAreaEmit.Emit, the cat track tool's definition). It validates and applies each chunk before the next. The game's Temp
// areas are mapped back to candidates by geometry (QuickRunsRules.MatchRing: the engine recycles Temps, an entity never identifies a
// candidate). Apply is all-or-nothing per emission (ToolBaseSystem.cs:533-540), so a refused run is dropped and the chunk's survivors
// re-emitted once (QuickRunsRules.AfterValidation). Right click (or the hub's Stop) stops at the next chunk boundary. What is built stays
// one undoable batch. Closing the tool keeps what is built. The runs bind and pay like drawn runs (SkiRunOwnershipSystem charges once at
// bind: SkiRunBinding FlagPaid + m_PaidAmount). Undo deletes the batch the bulldozer's way and pays back exactly what each binding recorded
// (QuickRunsRules.RefundOf, then PlayerMoney.Add).
// Probes ([probe], Measurement diagnostics only): snapshot parity, stage costs, the emission, the Temp mapping, binds and charges, game
// checks off (Anarchy).
// Engine notes: EngineNotes/QuickRunsToolSystem.md (Build, Undo and refund, Probes)

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Colossal.Entities;
using Game.City;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using SkiResortMod.Settings;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class QuickRunsToolSystem
    {
        private const int TempTimeoutFrames = 30;   // the cat track tool's (and LiftLineToolSystem's) value
        private const int ScanAfterUndoFrames = 2;  // margin only: the scan's area query already skips the runs the Undo marked Deleted
        private int m_ScanAfterUndoFrame = -1;     // the frame of the scan after an Undo, -1 = none
        private const int BindCheckFrames = 150;    // the bind line: ownership binds new runs within its sweep (64 frames) + margin
        /// <summary>Areas (pistes and cat tracks) per emission. Smoothness over speed: a validation and a binding wave per chunk, never the
        /// whole batch in one frame. First field value (CRITIQUE F6: each chunk's Apply is a full sweep + ratings in one frame). A runs-first
        /// plan of about 170 areas takes about 85 chunks. To measure (Measurement diagnostics, the bridge sheet): build a full plan and read
        /// the [probe] build chunk lines and the [probe] build summary line (frames and wall ms per chunk, the worst chunk). The value may
        /// rise while the worst chunk stays under one long frame (about 50 ms).</summary>
        internal const int BuildChunkRuns = 2;

        private struct BatchRun
        {
            public Entity Run, Prefab;
            public int Id, Fee;
        }

        private readonly List<int> m_EmitIdx = new List<int>();            // result indices of this emission
        private readonly List<float2[]> m_EmitRings = new List<float2[]>();
        private readonly List<Entity> m_AppliedTemps = new List<Entity>();
        private readonly List<int> m_AppliedIdx = new List<int>();
        private readonly List<BatchRun> m_Batch = new List<BatchRun>();
        private readonly List<BatchRun> m_BindCheck = new List<BatchRun>();
        private readonly List<int> m_BuildQueue = new List<int>();          // result indices not emitted yet
        private int m_EmitFrame, m_BindCheckFrame = -1, m_BuildTotal, m_BuildDone, m_BuildChunk, m_ChunkFrame, m_BuildTracks, m_ChunkMaxFrames;
        private double m_ChunkMaxMs;
        private int m_BuildStartFrame;
        private bool m_Retried, m_BatchFresh, m_BuildNextFrame, m_StopBuild, m_BuildPartial;
        private long m_BatchPaidSeen, m_BuildFee, m_BuildStartTicks, m_ChunkStartTicks;
        private string m_EmitWhy = "retry";

        /// <summary>Runs built so far / runs in this build (the hub's and the tooltip's "Building runs N / M").</summary>
        internal int BuildDone => m_BuildDone;
        internal int BuildTotal => m_BuildTotal;
        internal bool Building => m_Phase == ToolPhase.Emitted || m_Phase == ToolPhase.Retry || m_Phase == ToolPhase.Applying;


        /// <summary>Enter / the hub's Build: refuse, or emit every enabled candidate.</summary>
        private void BuildFromTool(string why)
        {
            if (m_Phase != ToolPhase.Preview || m_Published.Status != AutoPlanStatus.Ok)
            {
                Say($"QuickRuns [build]: nothing to build ({why}) — scan a region first", NoticeKey("NEED_SCAN"));
                return;
            }
            if (m_TreesOn)
            {
                // Tree definitions and run definitions never share a frame: clear the tree preview, build next frame
                SetTrees(false, "build");
                m_BuildNextFrame = true;
                return;
            }
            if (!WorldUnchanged(out string changed))
            {
                Say($"QuickRuns [build]: the world changed since the scan ({changed}) — scanning again ({why})", NoticeKey("RESCANNED"));
                BeginScan("world changed");
                return;
            }
            RefreshEnabled();
            if (m_Totals.Enabled == 0)
            {
                Say($"QuickRuns [build]: every run is left out ({why})", NoticeKey("NOTHING_ENABLED"));
                return;
            }
            long money = CityMoney();
            long lifts = LiftCostOn();   // plan 3.3: the city must pay the runs and the lifts switched on before anything is built
            if (!QuickRunsRules.CanPay(money, m_Totals.Fee + lifts))
            {
                Say($"QuickRuns [build]: REFUSED — the batch costs {m_Totals.Fee} (runs) + {lifts} (lifts), the city has {money} ({why})",
                    QuickRunsRules.TipPrefix + "WARN[" + QuickToolWarning.MoneyShort + "]");
                return;
            }
            AutoPlanResult r = m_Published;
            m_BuildQueue.Clear();
            int noPrefab = 0;
            for (int i = 0; i < r.Runs.Count; i++)
            {
                if (!IsEnabled(i))
                    continue;
                if (PrefabOf(r.Runs[i].Kind) == Entity.Null)
                {
                    noPrefab++;
                    continue;
                }
                m_BuildQueue.Add(i);
            }
            // Plan 7.6: the cat tracks first (a piste may lie over a cat track, the game checks them apart: SkiRunAreaTypeSystem), then the
            // pistes, each in the result's order
            int tracksFirst = 0;
            for (int q = 0; q < m_BuildQueue.Count; q++)
            {
                int i = m_BuildQueue[q];
                if (RunFeatures.FromKind((int)r.Runs[i].Kind).Base != RunBase.CatTrack)
                    continue;
                m_BuildQueue.RemoveAt(q);
                m_BuildQueue.Insert(tracksFirst++, i);
            }
            m_BuildTracks = tracksFirst;
            if (m_BuildQueue.Count == 0)
            {
                Say($"QuickRuns [build]: REFUSED — the run prefabs of every enabled run are missing this load ({noPrefab})",
                    NoticeKey("RUNS_MISSING"));
                return;
            }
            m_BuildFee = m_Totals.Fee;
            m_Refused.Clear();
            m_Batch.Clear();
            m_BatchFresh = false;
            m_BuildTotal = m_BuildQueue.Count;
            m_BuildDone = m_BuildChunk = 0;
            m_StopBuild = m_BuildPartial = false;
            m_BuildStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            m_BuildStartFrame = m_Frame;
            m_ChunkMaxMs = 0.0;
            m_ChunkMaxFrames = 0;
            Say($"QuickRuns [build]: START — {m_BuildTotal} area(s) ({m_BuildTracks} cat track(s) first, then {m_BuildTotal - m_BuildTracks} piste(s)) in chunks of {BuildChunkRuns} ({why}" +
                $"{(noPrefab > 0 ? $", {noPrefab} run(s) skipped: prefab missing" : string.Empty)}), fee {m_BuildFee}");
            TakeChunk();
            EmitBatch(m_EmitWhy);
        }

        /// <summary>Move the next chunk off the queue into m_EmitIdx (the caller emits it now, or next frame through phase Retry).</summary>
        private void TakeChunk()
        {
            m_EmitIdx.Clear();
            while (m_EmitIdx.Count < BuildChunkRuns && m_BuildQueue.Count > 0)
            {
                m_EmitIdx.Add(m_BuildQueue[0]);
                m_BuildQueue.RemoveAt(0);
            }
            m_Retried = false;
            m_BuildChunk++;
            m_ChunkStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            m_ChunkFrame = m_Frame;
            m_EmitWhy = $"chunk {m_BuildChunk}";
        }

        /// <summary>Right click or the hub's Stop while building: the chunk in flight finishes, nothing after it is emitted.</summary>
        private void RequestStopBuild(string why)
        {
            if (!Building || m_StopBuild)
                return;
            m_StopBuild = true;
            Say($"QuickRuns [build]: stop requested ({why}) — the chunk in flight finishes, {m_BuildQueue.Count} run(s) will not be built");
            Bump();
        }

        /// <summary>One definition per run of m_EmitIdx, all in this frame.</summary>
        private void EmitBatch(string why)
        {
            AutoPlanResult r = m_Published;
            TerrainHeightData hd = m_TerrainSystem.GetHeightData();
            EntityCommandBuffer ecb = m_Barrier.CreateCommandBuffer();
            m_EmitRings.Clear();
            float area = 0f;
            foreach (int i in m_EmitIdx)
            {
                AutoRun run = r.Runs[i];
                RunAreaEmit.Emit(ecb, PrefabOf(run.Kind), run.Ring, ref hd);
                m_EmitRings.Add(run.Ring);
                area += run.AreaM2;
            }
            m_Phase = ToolPhase.Emitted;
            m_EmitFrame = m_Frame;
            Bump();
            Say(string.Format(CultureInfo.InvariantCulture,
                "QuickRuns [build]: {0} definition(s) emitted in one frame ({1}; {2} of {3} built before it) — {4:F1} ha; waiting for the " +
                "game's Temp areas and validation",
                m_EmitIdx.Count, why, m_BuildDone, m_BuildTotal, area / 10000f));
        }

        /// <summary>Emitted: wait for one Temp per emission (or the timeout), map each to its candidate, then Apply / Retry / GiveUp.</summary>
        private void UpdateEmitted()
        {
            var temps = new List<Entity>();
            CollectRunTemps(temps);
            int waited = m_Frame - m_EmitFrame;
            if (temps.Count < m_EmitIdx.Count && waited <= TempTimeoutFrames)
                return;
            AutoPlanResult r = m_Published;
            var tempOf = new Entity[m_EmitIdx.Count];
            var refused = new bool[m_EmitIdx.Count];
            int unmatched = 0, unmatchedErrors = 0, errors = 0;
            foreach (Entity t in temps)
            {
                float2[] ring = RunAreaEmit.ReadRing(EntityManager, t, out _);
                int k = QuickRunsRules.MatchRing(ring, m_EmitRings);
                bool error = EntityManager.HasComponent<Error>(t);
                if (error)
                    errors++;
                if (k < 0 || tempOf[k] != Entity.Null)
                {
                    unmatched++;
                    if (error)
                        unmatchedErrors++;
                    continue;
                }
                tempOf[k] = t;
                if (error)
                    refused[k] = true;
            }
            int refusedCount = 0, missing = 0;
            for (int k = 0; k < tempOf.Length; k++)
            {
                if (tempOf[k] == Entity.Null)
                {
                    refused[k] = true;
                    missing++;
                }
                if (refused[k])
                    refusedCount++;
            }
            // An error on some other Temp (not one of ours) blocks the Apply too (ToolBaseSystem.cs:533-540)
            bool otherErrors = refusedCount == 0 && unmatchedErrors == 0 && !GetAllowApply();
            if (SkiResortSettings.DiagnosticsOn)
                ProbeEmission(temps.Count, waited, errors, unmatched, missing, tempOf);
            QuickBuildStep step = unmatchedErrors > 0 || otherErrors
                ? QuickBuildStep.GiveUp
                : QuickRunsRules.AfterValidation(m_EmitIdx.Count, refusedCount, m_Retried);
            string refusedText = RefusedText(r, refused);
            switch (step)
            {
                case QuickBuildStep.Apply:
                    applyMode = ApplyMode.Apply;   // ToolOutputSystem.cs:22-28, then ApplyAreasSystem keeps each Temp as the run (:204-208)
                    m_Deps = DestroyDefinitions(m_DefinitionQuery, m_Barrier, m_Deps);
                    m_AppliedTemps.Clear();
                    m_AppliedIdx.Clear();
                    for (int k = 0; k < tempOf.Length; k++)
                    {
                        m_AppliedTemps.Add(tempOf[k]);
                        m_AppliedIdx.Add(m_EmitIdx[k]);
                    }
                    m_Phase = ToolPhase.Applying;
                    QuickGameChecks checks = GameChecks();
                    Say($"QuickRuns [build]: APPLY — {tempOf.Length} run(s) validated after {waited} frame(s){(m_Retried ? " (second emission)" : string.Empty)}" +
                        (QuickRunsRules.ChecksOff(checks) ? $"; game checks are OFF ({ChecksText(checks)}): only the planner's checks protected this build" : string.Empty));
                    break;
                case QuickBuildStep.Retry:
                    for (int k = m_EmitIdx.Count - 1; k >= 0; k--)
                    {
                        if (!refused[k])
                            continue;
                        m_Refused.Add(r.Runs[m_EmitIdx[k]].Id);
                        m_EmitIdx.RemoveAt(k);
                    }
                    applyMode = ApplyMode.Clear;   // ToolClearSystem deletes our Temps (ToolClearSystem.cs:101)
                    m_Deps = DestroyDefinitions(m_DefinitionQuery, m_Barrier, m_Deps);
                    m_Retried = true;
                    m_EmitWhy = $"chunk {m_BuildChunk} retry";
                    m_Phase = ToolPhase.Retry;
                    RefreshEnabledKeepPhase();
                    m_BuildFee = m_Totals.Fee;
                    Say($"QuickRuns [build]: {refusedCount} run(s) refused by the game ({refusedText}) — dropped; re-emitting the other " +
                        $"{m_EmitIdx.Count} once", NoticeKey("DROPPED"));
                    break;
                default:
                    for (int k = 0; k < refused.Length; k++)
                        if (refused[k])
                            m_Refused.Add(r.Runs[m_EmitIdx[k]].Id);
                    applyMode = ApplyMode.Clear;
                    m_Deps = DestroyDefinitions(m_DefinitionQuery, m_Barrier, m_Deps);
                    if (m_BuildDone > 0)
                    {
                        // Earlier chunks are permanent: keep them as the batch and stop here (never a half-known state)
                        Mod.Log.Warn($"QuickRuns [build]: chunk {m_BuildChunk} refused ({refusedCount} of {tempOf.Length}: {refusedText}" +
                                     $"{(otherErrors ? "; an error on another preview object" : string.Empty)}) — stopping with {m_BuildDone} run(s) built");
                        m_BuildPartial = true;
                        FinishBuild();
                        break;
                    }
                    m_Phase = ToolPhase.Preview;
                    RefreshEnabled();
                    string reason = unmatchedErrors > 0 || otherErrors
                        ? $"{(otherErrors ? "an error on another preview object" : unmatchedErrors + " unmatched preview run(s) with errors")}"
                        : $"{refusedCount} of {tempOf.Length} refused{(m_Retried ? " on the second emission" : string.Empty)} ({refusedText})";
                    Say($"QuickRuns [build]: NOTHING BUILT — {reason}; the refused runs are red and left out, Enter tries the rest",
                        NoticeKey("REFUSED"));
                    break;
            }
        }

        /// <summary>RefreshEnabled while the phase is Retry (RefreshEnabled reads the result only in Preview).</summary>
        private void RefreshEnabledKeepPhase()
        {
            ToolPhase keep = m_Phase;
            m_Phase = ToolPhase.Preview;
            RefreshEnabled();
            m_Phase = keep;
        }

        /// <summary>The refused candidates as "#id kind band" (ids are the preview ids the log uses everywhere).</summary>
        private string RefusedText(AutoPlanResult r, bool[] refused)
        {
            var sb = new StringBuilder();
            for (int k = 0; k < refused.Length && sb.Length < 200; k++)
            {
                if (!refused[k])
                    continue;
                if (sb.Length > 0)
                    sb.Append(", ");
                AutoRun run = r.Runs[m_EmitIdx[k]];
                sb.Append('#').Append(run.Id).Append(' ').Append(run.Band).Append(' ').Append(run.Kind);
            }
            return sb.Length > 0 ? sb.ToString() : "none";
        }

        /// <summary>The planned kind's sliders on its new groomed run (run types R3): the placer's defaults for that kind (snowmaking:
        /// guns, night: guns and lights, groomed and natural: none), and the steps settled, so the run pays what the kind's fee always
        /// was.</summary>
        private void SeedFeatures(Entity run, RunKind kind)
        {
            int gun = RunPropPlacerRules.DefaultGunSpacing((int)kind), light = RunPropPlacerRules.DefaultLightSpacing((int)kind);
            if (gun == RunPropPlacerRules.SpacingOff && light == RunPropPlacerRules.SpacingOff)
                return;
            if (m_RunProps.SeedSpacing(run, gun, light))
                m_Ownership.SettleFeatureSteps(run, "quick runs");
        }

        /// <summary>Applying (the frame after Apply): add the chunk's permanent runs to the batch. The next chunk emits on the next frame
        /// (phase Retry), or the build finishes (queue empty, or a stop was requested).</summary>
        private void FinishApplied()
        {
            AutoPlanResult r = m_Published;
            int lost = 0, before = m_Batch.Count;
            for (int k = 0; k < m_AppliedTemps.Count; k++)
            {
                Entity e = m_AppliedTemps[k];
                if (!EntityManager.Exists(e) || EntityManager.HasComponent<Temp>(e) || EntityManager.HasComponent<Deleted>(e) ||
                    !EntityManager.TryGetComponent(e, out PrefabRef pr))
                {
                    lost++;
                    continue;
                }
                AutoRun run = r.Runs[m_AppliedIdx[k]];
                SeedFeatures(e, run.Kind);
                m_Batch.Add(new BatchRun { Run = e, Prefab = pr.m_Prefab, Id = run.Id, Fee = QuickRunsRules.FeeOf(run.Kind, run.AreaM2) });
            }
            m_BuildDone += m_Batch.Count - before;
            m_AppliedTemps.Clear();
            m_AppliedIdx.Clear();
            m_BatchFresh = m_Batch.Count > 0;   // undoable from the first chunk on
            if (lost > 0)
                Mod.Log.Warn($"QuickRuns [build]: chunk {m_BuildChunk}: {lost} run(s) NOT permanent the frame after Apply (please send the log)");
            double chunkMs = QuickRunsRules.MsSince(m_ChunkStartTicks);
            m_ChunkMaxMs = Math.Max(m_ChunkMaxMs, chunkMs);
            m_ChunkMaxFrames = Math.Max(m_ChunkMaxFrames, m_Frame - m_ChunkFrame);
            if (SkiResortSettings.DiagnosticsOn)
                Mod.Log.Info(string.Format(CultureInfo.InvariantCulture,
                    "QuickRuns [probe] build chunk {0}: {1} run(s) applied, {2} of {3} built; {4:F0} ms wall over {5} frame(s)",
                    m_BuildChunk, m_Batch.Count - before, m_BuildDone, m_BuildTotal, chunkMs, m_Frame - m_ChunkFrame));
            Bump();
            if (m_BuildQueue.Count > 0 && !m_StopBuild)
            {
                TakeChunk();
                m_Phase = ToolPhase.Retry;   // emits on the next frame: this frame's Apply settles first
                return;
            }
            FinishBuild();
        }


        /// <summary>The build ends (done, stopped, or a later chunk refused): the batch, the bind line, the preview gone.</summary>
        private void FinishBuild()
        {
            m_BindCheck.Clear();
            m_BindCheck.AddRange(m_Batch);
            m_BindCheckFrame = m_Frame + BindCheckFrames;
            m_BatchFresh = m_Batch.Count > 0;
            int notBuilt = m_BuildTotal - m_BuildDone;
            bool stopped = m_StopBuild && notBuilt > 0;
            m_BuildQueue.Clear();
            m_OffIds.Clear();
            m_Refused.Clear();
            m_Phase = ToolPhase.Region;
            DropPreview(null);
            LastBuildText = $"{m_Batch.Count} of {m_BuildTotal} run(s) in {m_BuildChunk} chunk(s)" +
                            $"{(stopped ? ", stopped" : m_BuildPartial ? ", partly refused" : string.Empty)}";
            string wall = string.Format(CultureInfo.InvariantCulture, "{0:F0} ms wall", QuickRunsRules.MsSince(m_BuildStartTicks));
            string notice = stopped ? NoticeKey("STOPPED") : m_BuildPartial ? NoticeKey("PARTIAL") : NoticeKey("BUILT");
            Say($"QuickRuns [build]: {(stopped ? "STOPPED" : m_BuildPartial ? "PARTLY BUILT" : "BUILT")} — {m_Batch.Count} of {m_BuildTotal} " +
                $"run(s) permanent in {m_BuildChunk} chunk(s), {wall} (they bind to the nearest Ski Area Welcome Sign and pay their fees like " +
                $"drawn runs; bind line in {BindCheckFrames} frames); the hub's Undo removes the batch with a refund (right click does not)", notice);
            if (SkiResortSettings.DiagnosticsOn && m_BuildChunk > 0)
                Mod.Log.Info(string.Format(CultureInfo.InvariantCulture,
                    "QuickRuns [probe] build summary: {0} chunk(s) of {1}, {2} cat track(s) and {3} piste(s) queued, {4:F0} ms wall ({5:F0} ms and " +
                    "{6:F1} frame(s) per chunk on average), worst chunk {7:F0} ms wall, most frames {8}", m_BuildChunk, BuildChunkRuns, m_BuildTracks,
                    m_BuildTotal - m_BuildTracks, QuickRunsRules.MsSince(m_BuildStartTicks), QuickRunsRules.MsSince(m_BuildStartTicks) / m_BuildChunk,
                    (m_Frame - m_BuildStartFrame) / (double)m_BuildChunk, m_ChunkMaxMs, m_ChunkMaxFrames));
            m_StopBuild = m_BuildPartial = false;
            Bump();
            // The lifts do not follow on their own: the hub's "Build lifts" starts the queue (the strings say so). The queue makes the station
            // placer the active tool, which closes this tool and ends its Undo, so a chained start would take the Undo of every run build.
        }

        /// <summary>Every frame (QuickRunsHotkeySystem): the tool's frame counter while closed, and the bind line of the last batch.</summary>
        internal void TickWatch()
        {
            if (!IsActive)
                m_Frame++;
            if (m_Notice != null && m_Frame - m_NoticeFrame == NoticeFrames)
                Bump();   // the notice expired: the hub drops it too
            if (m_BindCheckFrame < 0 || m_Frame < m_BindCheckFrame)
                return;
            m_BindCheckFrame = -1;
            int bound = 0, paid = 0, gone = 0;
            long sum = 0;
            var gates = new HashSet<Entity>();
            foreach (BatchRun b in m_BindCheck)
            {
                if (!EntityManager.Exists(b.Run) || EntityManager.HasComponent<Deleted>(b.Run))
                {
                    gone++;
                    continue;
                }
                if (!EntityManager.TryGetComponent(b.Run, out SkiRunBinding binding))
                    continue;
                if (binding.m_Gate != Entity.Null)
                {
                    bound++;
                    gates.Add(binding.m_Gate);
                }
                if ((binding.m_Flags & SkiRunBinding.FlagPaid) != 0)
                {
                    paid++;
                    sum += QuickRunsRules.RefundOf(true, true, (binding.m_Flags & SkiRunBinding.FlagRestored) != 0, binding.m_PaidAmount);
                }
            }
            m_BatchPaidSeen = sum;
            Mod.Log.Info($"QuickRuns [bind]: {bound} of {m_BindCheck.Count} run(s) bound to {gates.Count} sign(s), {paid} paid, charged {sum} " +
                         $"(preview total {m_BuildFee}{(sum == m_BuildFee ? ", equal" : ", DIFFERENT: the area the bind measured differs from the plan's")})" +
                         $"{(gone > 0 ? $", {gone} already gone" : string.Empty)}");
            if (SkiResortSettings.DiagnosticsOn)
                ProbeBinds();
            Bump();
        }


        /// <summary>A run of the batch was re-emitted (SkiRunReemitSystem): the undo and the bind line follow the new entity and prefab, so
        /// an undo deletes the run that stands and never leaves the old one's slot to a duplicate.</summary>
        internal void RekeyRun(Entity from, Entity to, Entity toPrefab)
        {
            RekeyIn(m_Batch, from, to, toPrefab);
            RekeyIn(m_BindCheck, from, to, toPrefab);
        }

        private static void RekeyIn(List<BatchRun> list, Entity from, Entity to, Entity toPrefab)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Run != from)
                    continue;
                BatchRun b = list[i];
                b.Run = to;
                b.Prefab = toPrefab;
                list[i] = b;
            }
        }

        /// <summary>Delete the batch (the bulldozer's shape) and pay back what each binding recorded as paid.</summary>
        private void UndoBatch(string why)
        {
            if (m_Batch.Count == 0)
            {
                Say($"QuickRuns [undo]: nothing to undo ({why})", NoticeKey("NOTHING_TO_UNDO"));
                return;
            }
            long refund = 0;
            int deleted = 0, skipped = 0, unbound = 0;
            foreach (BatchRun b in m_Batch)
            {
                if (!EntityManager.Exists(b.Run) || EntityManager.HasComponent<Deleted>(b.Run) ||
                    !EntityManager.TryGetComponent(b.Run, out PrefabRef pr) || pr.m_Prefab != b.Prefab)
                {
                    skipped++;   // bulldozed (no refund: the bulldozer refunds no run fee either) or the index was reused
                    continue;
                }
                bool has = EntityManager.TryGetComponent(b.Run, out SkiRunBinding binding);
                if (!has)
                    unbound++;
                refund += QuickRunsRules.RefundOf(has, has && (binding.m_Flags & SkiRunBinding.FlagPaid) != 0,
                    has && (binding.m_Flags & SkiRunBinding.FlagRestored) != 0, has ? binding.m_PaidAmount : 0);
                RunAreaEmit.DeleteLikeBulldozer(EntityManager, b.Run);
                deleted++;
            }
            bool paidBack = Refund(refund);
            int count = m_Batch.Count;
            m_Batch.Clear();
            m_BindCheck.Clear();
            m_BindCheckFrame = -1;
            m_BatchFresh = false;
            // The build dropped the plan (FinishBuild), so Build stayed gray after an Undo until the player scanned again (field pass
            // 2026-11-10). The region is still there: scan it again on its own. The obstacles then match the scan before the build, so the
            // scan job's plan cache gives that plan back (realize only). The scan replaces the lift sheet, as a hand scan does.
            bool rescan = ScanAfterUndoPossible(out string noScan);
            if (rescan)
                m_ScanAfterUndoFrame = m_Frame + ScanAfterUndoFrames;
            Say($"QuickRuns [undo]: UNDONE ({why}) — {deleted} of {count} run(s) deleted, {skipped} already gone, {unbound} not bound yet " +
                $"(never charged); refund {refund}{(paidBack ? string.Empty : " NOT PAID: the city has no PlayerMoney")}; " +
                (rescan ? $"the region is scanned again in {ScanAfterUndoFrames} frame(s)" : "no scan: " + noScan),
                NoticeKey(rescan ? "UNDONE_RESCAN" : "UNDONE"));
            Bump();
        }

        /// <summary>Whether an Undo can bring the plan back with a scan of the same region: corners closed, no tree filler (a scan turns it
        /// off), no build or tree cycle in flight. A scan in flight is restarted (it read the undone runs as obstacles).</summary>
        private bool ScanAfterUndoPossible(out string why)
        {
            why = !RegionClosed ? "the region is not closed"
                : m_TreesOn ? "the tree filler is on"
                : Building ? "a build runs"
                : m_Planting || RemovingTrees ? "trees are planted or removed"
                : m_Phase != ToolPhase.Region && m_Phase != ToolPhase.Preview && m_Phase != ToolPhase.Scanning ? "phase " + m_Phase
                : null;
            return why == null;
        }

        /// <summary>ScanAfterUndoFrames after the hub's Undo: scan the region again. The state is checked again (the player may have
        /// cleared the corners or turned the tree filler on in between).</summary>
        private void ScanAfterUndo()
        {
            m_ScanAfterUndoFrame = -1;
            if (!ScanAfterUndoPossible(out string why))
            {
                Say($"QuickRuns [undo]: no scan after the undo — {why}");
                return;
            }
            if (m_Phase == ToolPhase.Scanning)
                DropPreview(null);   // that scan read the undone runs as obstacles
            BeginScan("undo");
        }

        /// <summary>PlayerMoney.Add on the City entity (SkiRunOwnershipSystem.Charge's mirror). False without a city / money.</summary>
        private bool Refund(long amount)
        {
            if (amount <= 0)
                return true;
            Entity city = m_CitySystem.City;
            if (city == Entity.Null || !EntityManager.TryGetComponent(city, out PlayerMoney money))
                return false;
            money.Add((int)math.min(amount, int.MaxValue));
            EntityManager.SetComponentData(city, money);
            return true;
        }

        private void ClearBatch(string why)
        {
            m_Batch.Clear();
            m_BatchFresh = false;
            m_BuildNextFrame = false;
            m_BuildQueue.Clear();
            m_BuildTotal = m_BuildDone = 0;
            m_StopBuild = m_BuildPartial = false;
            m_AppliedTemps.Clear();
            m_AppliedIdx.Clear();
            m_EmitIdx.Clear();
            m_EmitRings.Clear();
            if (why == "load")
            {
                m_BindCheck.Clear();
                m_BindCheckFrame = -1;
                m_BatchPaidSeen = 0;
            }
        }

        private void CollectRunTemps(List<Entity> into)
        {
            if (m_TempAreaQuery.IsEmptyIgnoreFilter)
                return;
            using (var areas = m_TempAreaQuery.ToEntityArray(Allocator.Temp))
            using (var prefabs = m_TempAreaQuery.ToComponentDataArray<PrefabRef>(Allocator.Temp))
            {
                for (int i = 0; i < areas.Length; i++)
                    if (IsOurRunPrefab(prefabs[i].m_Prefab))
                        into.Add(areas[i]);
            }
        }

        private int DeleteRunTemps()
        {
            var temps = new List<Entity>();
            CollectRunTemps(temps);
            foreach (Entity t in temps)
                EntityManager.AddComponent<Deleted>(t);
            return temps.Count;
        }


        // ---------------------------------------------------------------- probes (Measurement diagnostics only)

        /// <summary>The scan's proof lines (round 2's cross-runtime gate): the plan hash, the options and inputs prints, the planner's
        /// per-pass ms, the realizer's ms by stage, the total, the blocked cells, what the realizer kept. Then the snapshot parity (the
        /// planner's sampler vs TerrainUtils.SampleHeight at 64 points of the region).</summary>
        private void ProbeScan(QuickRunsScanJob job, double ms)
        {
            if (job != null)
            {
                RealizeReport rep = job.Report;
                QuickRunsScanJob.Planned p = job.Plan;
                Mod.Log.Info(string.Format(CultureInfo.InvariantCulture,
                    "QuickRuns [probe] runs-first {0}: plan {1}, result {2:x16}, options {3:x16} (density {4}), inputs {5}; planner {6} ms " +
                    "[{7}]; blocked cells {8} of {9}; realizer {10:F0} ms (lattice {11:F0}, lifts {12:F0}, pistes {13:F0}, tracks {14:F0}, graph {15:F0}), " +
                    "checks {16:F0} ms, job {17:F0} ms, scan {18:F0} ms wall; kept piste km {19:F2} of {20:F2}, track km {21:F2} of {22:F2}, " +
                    "unserved {23}, lifts clear {24} of {25}, layers {26:F1} MB",
                    job.Id, p == null ? "none" : p.Hash.ToString("x16", CultureInfo.InvariantCulture) + (job.FromCache ? " (cached)" : string.Empty),
                    job.Result?.Fingerprint ?? 0UL, job.Options.Fingerprint(), m_Knobs.Density,
                    job.PlannerInputs == null ? "none (bare)" : job.PlannerInputs.Fingerprint().ToString("x16", CultureInfo.InvariantCulture),
                    p == null ? "-" : p.PlanMs.ToString("F0", CultureInfo.InvariantCulture), job.PassText(), p?.BlockedCells ?? 0,
                    p == null ? 0 : p.BlockedCells + p.OpenCells, job.RealizeMs, rep.LatticeMs, rep.LiftMs, rep.PisteMs, rep.TrackMs, rep.GraphMs,
                    job.PrecheckMs, job.TotalMs, ms, rep.PisteKmKept + rep.GradedKmKept, rep.PisteKmPlanned + rep.GradedKmPlanned, rep.TrackKmKept,
                    rep.TrackKmPlanned, rep.UnservedAreas, rep.LiftsClear, rep.Lifts, rep.LayerBytes / 1048576.0));
                // the cross-runtime prints (port round 2b-f): rf-replay on a dump of this scan names the first pass that differs from the host
                Mod.Log.Info($"QuickRuns [probe] runs-first prints {job.Id}: plan {(p == null ? "none" : p.Hash.ToString("x16", CultureInfo.InvariantCulture))}; {job.PrintsText()}");
            }
            TerrainHeightData hd = m_TerrainSystem.GetHeightData();
            float worst = 0f;
            int n = 0;
            if (hd.isCreated && m_Snap != null)
            {
                Colossal.Mathematics.Bounds2 b = SnapBounds();
                float2 size = b.max - b.min;
                for (int j = 1; j <= 8; j++)
                {
                    for (int i = 1; i <= 8; i++)
                    {
                        var pt = new float3(b.min.x + size.x * i / 9f, 0f, b.min.y + size.y * j / 9f);
                        float a = m_Snap.Sample(pt), g = TerrainUtils.SampleHeight(ref hd, pt);
                        if (!math.isfinite(a))
                            continue;
                        worst = math.max(worst, math.abs(a - g));
                        n++;
                    }
                }
            }
            Mod.Log.Info(string.Format(CultureInfo.InvariantCulture,
                "QuickRuns [probe] scan: snapshot parity max |snapshot - SampleHeight| {0:G3} m over {1} point(s) (0 expected: same float ops); " +
                "{2:F0} ms wall over {3} frame(s) (Mono)", worst, n, ms, m_ScanFrames));
        }

        private void ProbeEmission(int temps, int waited, int errors, int unmatched, int missing, Entity[] tempOf)
        {
            var sb = new StringBuilder();
            for (int k = 0; k < tempOf.Length && k < 24; k++)
            {
                if (k > 0)
                    sb.Append(' ');
                sb.Append(k).Append("->").Append(tempOf[k] == Entity.Null ? "none" : Describe(tempOf[k]));
            }
            // The planner refused these for what the game also checks (an obstacle: any Surface area or an unowned tile, the region / map
            // edge, a self-crossing outline). With the game's checks off, the game would have accepted them. Only the pre-check stood.
            AutoPlanResult r = m_Published;
            int preRefused = r.Rejects[(int)AutoReject.Obstacle] + r.Rejects[(int)AutoReject.Region] + r.Rejects[(int)AutoReject.Crosses];
            QuickGameChecks checks = GameChecks();
            Mod.Log.Info($"QuickRuns [probe] emission: {m_EmitIdx.Count} definition(s) emitted in frame {m_EmitFrame}, {temps} Temp run(s) after " +
                         $"{waited} frame(s); errors {errors}, unmatched {unmatched}, missing {missing}; mapping {sb}; game checks " +
                         $"{(QuickRunsRules.ChecksOff(checks) ? "OFF (" + ChecksText(checks) + ")" : "on")}; pre-check refused {preRefused} " +
                         $"candidate(s) (obstacle / region / crossing){(QuickRunsRules.ChecksOff(checks) && preRefused > 0 ? ", which the game would have accepted" : string.Empty)}");
        }

        /// <summary>The log text of a game-checks state (developer lines only).</summary>
        private static string ChecksText(QuickGameChecks checks)
        {
            switch (checks)
            {
                case QuickGameChecks.OverlapDisabled: return "the OverlapExisting error prefab is disabled in game: a mod turned the check off";
                case QuickGameChecks.NoOverlapPrefab: return "no OverlapExisting error prefab";
                case QuickGameChecks.IgnoreErrors: return "ToolSystem.ignoreErrors: errors may still be flagged, Apply ignores them";
                default: return "on";
            }
        }

        private void ProbeBinds()
        {
            var sb = new StringBuilder();
            int k = 0;
            foreach (BatchRun b in m_BindCheck)
            {
                if (k++ >= 24)
                    break;
                bool has = EntityManager.Exists(b.Run) && EntityManager.HasComponent<SkiRunBinding>(b.Run);
                SkiRunBinding bd = has ? EntityManager.GetComponentData<SkiRunBinding>(b.Run) : default;
                sb.Append(' ').Append(Describe(b.Run)).Append(" fee ").Append(b.Fee).Append(" paid ").Append(has ? bd.m_PaidAmount : 0)
                  .Append(" flags ").Append(has ? bd.m_Flags : 0).Append(';');
            }
            Mod.Log.Info($"QuickRuns [probe] binds:{sb}");
        }
    }
}
