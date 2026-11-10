// Quick runs tool: the scan job (runs-first since port round R2-a, plan 7.4). One QuickRunsScanJob per scan runs the runs-first planner
// and the realizer on a worker thread (Task.Run). The tree filler's compose job is the pattern (QuickRunsToolSystem.Trees.cs: state,
// launch bookkeeping with a GC count, stale test, per-frame poll and cancel, collect with IsFaulted and the stale drop, the summary fields).
// States, in order: Idle, Running (the worker), collected on the main thread, then Idle again. There is no main-thread path: runs-first
// cannot be sliced (plan 7.4), so the old Diagnostics fallback setting went with the v1 / v2 planner.
// The worker touches ONLY its job: a fresh AutoPlanInput per scan, a fresh obstacle list, a snapshot buffer, the planner inputs and the
// options. The buffer is never rewritten while a job holds it (TakeSnapshot writes the other one). No Entities, no Unity API and no log on
// the worker. The job owns its result, and no later job writes it, so m_Published is the job's result as it is. One job at a time. A scan
// requested while a canceled job is still stopping is queued. It is launched when that job returns (the planner's cancel returns within
// 90 ms, R1-a). Build, Undo and Plant never overlap a running job. The hub refuses build / undo / plant / trees while Scanning (ScanBlocks).
// The plan cache: the last two plans, keyed by snapshot, region, obstacles, existing lifts, access points, options and the bare flag
// (QuickRunsScanJob.PlanKey). A key the cache holds only realizes again (the Kinds knob, a density switched back).
// Existing lifts and access points are collected here on the main thread before the launch.
// Dev path (Measurement diagnostics): DevScan (scan a given ring, optionally bare) and DevDump (the last published scan's exact input
// and hashes to a file, replayed by the test host's rf-replay), called by QuickRunsDevUISystem.
// Logs (silence impossible): START / DONE (Scan.cs), CANCELED (here, every cancel of a running or queued scan), `!!!` on a faulted job
// (Fail: the tool is disabled until the next load, the v1 policy). The drop of a stale result logs only with Measurement diagnostics.
// Engine notes: EngineNotes/QuickRunsToolSystem.md (Scan, Worker), EngineNotes/ResortPlanner.md (Runs-first in the tool)

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Colossal.Collections;
using Colossal.Entities;
using Colossal.Mathematics;
using Game.Common;
using Game.Tools;
using SkiResortMod.Settings;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class QuickRunsToolSystem
    {
        /// <summary>Pinned lifts: a census lift counts when one of its chords comes within this of the region (spec 5.2 step 1).</summary>
        private const float PinnedReachM = 150f;
        /// <summary>Access points: road edges and the Welcome Sign within the region's bounds plus this (spec 2, AutoPlanInput.Access).</summary>
        private const float AccessReachM = 450f;
        private const float AccessStepM = 25f, PinnedSampleM = 10f;
        /// <summary>Plans the cache keeps (plan 7.4: the last two).</summary>
        private const int PlanCacheSize = 2;

        // The published result: the collected job's own result (no later job writes it)
        private AutoPlanResult m_Published = NoResult();
        private Task m_ScanTask;                 // the worker job; null = none in flight
        private QuickRunsScanJob m_ScanJob;      // the launched job (its progress, its result)
        private QuickRunsScanJob m_DoneJob;      // the last published job (the DONE line, the dump)
        private AutoPlanCancel m_ScanCancel;     // the cancel token of the launched job
        private int m_ScanGen, m_ScanLaunchGen;  // stale test: any cancel or new request bumps m_ScanGen
        private QuickRunsScanJob m_PendingJob;   // a scan requested while a canceled job was still stopping
        private ulong m_PendingKey, m_ScanKey;   // the plan cache keys of the pending and the launched job
        private string m_ScanJobWhy;             // the launched job's reason (m_ScanWhy may already name the queued one)
        private long m_ScanLaunchTicks, m_CollectTicks;
        private int m_ScanGc0, m_ScanMsFrame;
        private double m_ScanWorkerMs, m_ScanMainMs, m_ScanFrameMs, m_ScanFrameMaxMs, m_ContextMs;
        private readonly List<(ulong Key, QuickRunsScanJob.Planned Plan)> m_PlanCache = new List<(ulong, QuickRunsScanJob.Planned)>();
        private LiftCensusSystem m_LiftCensus;
        private Game.Net.SearchSystem m_NetSearch;
        private readonly List<LiftServedRule.Lift> m_ServeLifts = new List<LiftServedRule.Lift>();
        private readonly List<LiftServedRule.Chord> m_ServeChords = new List<LiftServedRule.Chord>();
        private readonly List<LiftCensus.Row> m_CensusRows = new List<LiftCensus.Row>();
        private readonly Dictionary<EntityRef, float3> m_StubTips = new Dictionary<EntityRef, float3>();
        // Dev path: a ring to scan (DevScan), the bare flag of the next scan, the region tag of the next scan
        private float2[] m_DevRing;
        private bool m_DevBare, m_NextBare;
        private string m_DevTag, m_NextTag = "region";

        private static AutoPlanResult NoResult()
        {
            var r = new AutoPlanResult();
            r.Clear();   // Status Running: no reader shows it
            return r;
        }

        /// <summary>A worker job has not returned yet (it may already be canceled).</summary>
        private bool ScanJobInFlight => m_ScanTask != null && !m_ScanTask.IsCompleted;

        /// <summary>Hub actions that change the world or the tree preview: refused while a scan runs (the phase machine of spec 5.2 step 7).</summary>
        private static bool ScanBlocks(string key) =>
            key == "build" || key == "undo" || key == "plant" || key == "treesUndo" || key == "trees";

        /// <summary>The cached plan for this key, or null.</summary>
        private QuickRunsScanJob.Planned CachedPlan(ulong key)
        {
            for (int i = 0; i < m_PlanCache.Count; i++)
                if (m_PlanCache[i].Key == key)
                    return m_PlanCache[i].Plan;
            return null;
        }

        private void CachePlan(ulong key, QuickRunsScanJob.Planned plan)
        {
            for (int i = m_PlanCache.Count - 1; i >= 0; i--)
                if (m_PlanCache[i].Key == key)
                    m_PlanCache.RemoveAt(i);
            m_PlanCache.Insert(0, (key, plan));
            while (m_PlanCache.Count > PlanCacheSize)
                m_PlanCache.RemoveAt(m_PlanCache.Count - 1);
        }


        // ---------------------------------------------------------------- launch

        /// <summary>A new scan (BeginScan): launched now, or queued behind a canceled job still stopping.</summary>
        private void StartScanJob(QuickRunsScanJob job, ulong key, long t0)
        {
            m_ScanGen++;
            ResetScanCounters();
            if (ScanJobInFlight)
            {
                m_ScanCancel?.Request();   // normally requested already (the drop that preceded this scan)
                m_PendingJob = job;
                m_PendingKey = key;
            }
            else
            {
                m_PendingJob = null;
                // BeginScan polled the job before its snapshot and obstacles, which take tens of ms on a large region. A worker that
                // finished since is collected here, so a fault is reported (Fail) instead of overwritten unseen. Its generation is already
                // old (m_ScanGen++ above), so a clean finish is dropped as out of date and nothing pending is launched.
                if (!PollScanJob(true))
                    return;
                LaunchScan(job, key);
            }
            AddScanMainMs(QuickRunsRules.MsSince(t0));
        }

        /// <summary>The job on a worker thread.</summary>
        private void LaunchScan(QuickRunsScanJob job, ulong key)
        {
            m_ScanJob = job;
            m_ScanKey = key;
            m_ScanCancel = job.Input.Cancel;
            m_Input = job.Input;
            m_PlannerSnap = job.Input.Snapshot;   // TakeSnapshot now writes the other buffer
            m_ScanLaunchGen = m_ScanGen;
            m_ScanJobWhy = m_ScanWhy;
            m_ScanLaunchTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            m_ScanGc0 = GC.CollectionCount(0);
            m_ScanWorkerMs = 0;
            m_ScanTask = Task.Run(() =>
            {
                long w0 = System.Diagnostics.Stopwatch.GetTimestamp();
                job.Run();
                m_ScanWorkerMs = QuickRunsRules.MsSince(w0);   // read after IsCompleted (the task's completion orders it)
            });
        }

        private void LaunchPending()
        {
            QuickRunsScanJob job = m_PendingJob;
            m_PendingJob = null;
            if (SkiResortSettings.DiagnosticsOn)
                Mod.Log.Info($"QuickRuns [probe] scan ({m_ScanWhy}) launched after the canceled scan stopped");
            LaunchScan(job, m_PendingKey);
        }

        /// <summary>A knob that changes the plan or its realization (Density, Kinds) changed in Preview or Scanning: a running job is
        /// canceled, and the region scans again. The plan cache makes a Kinds change a realization only.</summary>
        private void RescanForKnobs(string name)
        {
            if (m_Disabled || !PollScanJob(true))
                return;
            if (m_Phase == ToolPhase.Scanning)
            {
                CancelScanJob(name + " changed during the scan");
                m_Phase = ToolPhase.Region;
            }
            BeginScan(name);
        }


        // ---------------------------------------------------------------- per frame, cancel, collect

        /// <summary>One frame while Scanning (StepScan). True when the job ended this frame (published or dropped).</summary>
        private bool StepScanJob()
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            if (m_ScanTask != null)
            {
                if (!m_ScanTask.IsCompleted)
                {
                    AddScanMainMs(QuickRunsRules.MsSince(t0));   // the poll: well under 0.1 ms
                    return false;
                }
                CollectScanJob(true);
                return true;
            }
            if (m_PendingJob != null)
            {
                LaunchPending();
                return false;
            }
            Mod.Log.Warn("QuickRuns [scan]: phase Scanning without a job (contract drift?) — back to the region");
            m_Phase = ToolPhase.Region;
            Bump();
            return true;
        }

        /// <summary>Collects a finished job outside the per-frame poll (before a new scan). False when it had faulted.</summary>
        private bool PollScanJob(bool canClose)
        {
            if (m_ScanTask == null || !m_ScanTask.IsCompleted)
                return true;
            return CollectScanJob(canClose);
        }

        /// <summary>Stops the running or queued scan. The worker stops at its next poll and its result is dropped when it returns. Every
        /// cancel of something running writes one CANCELED line.</summary>
        private void CancelScanJob(string reason)
        {
            m_ScanGen++;
            if (m_PendingJob != null)
            {
                m_PendingJob = null;
                Mod.Log.Info($"QuickRuns [scan]: CANCELED ({m_ScanWhy}, queued) — {reason}; it never started");
            }
            bool worker = ScanJobInFlight && m_ScanCancel != null && !m_ScanCancel.Requested;
            if (!worker)
                return;
            m_ScanCancel.Request();
            Mod.Log.Info(string.Format(CultureInfo.InvariantCulture,
                "QuickRuns [scan]: CANCELED ({0}) — {1}; {2:F0} ms on the worker so far, progress {3:P0}; the worker stops at its next poll, its " +
                "result is dropped", m_ScanJobWhy, reason, QuickRunsRules.MsSince(m_ScanLaunchTicks), m_ScanJob?.Progress ?? 0f));
        }

        /// <summary>Main thread, the job is done. A fault disables the tool (the v1 Fail policy). A stale or canceled result is dropped
        /// (and a queued scan launches). Otherwise the plan is cached, the result published and the v1 epilogue runs (FinishScan). False
        /// after a fault.</summary>
        private bool CollectScanJob(bool canClose)
        {
            m_CollectTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            Task task = m_ScanTask;
            QuickRunsScanJob job = m_ScanJob;
            m_ScanTask = null;
            if (task != null && task.IsFaulted)
            {
                m_PendingJob = null;
                if (m_Phase == ToolPhase.Scanning)
                    m_Phase = ToolPhase.Region;
                Exception e = task.Exception != null && task.Exception.InnerExceptions.Count == 1 ? task.Exception.InnerException : task.Exception;
                Fail(e, "scan worker");
                if (canClose)
                {
                    applyMode = ApplyMode.Clear;
                    try { Deactivate(); } catch (Exception) { /* logged by Fail */ }
                }
                return false;
            }
            bool stale = m_ScanLaunchGen != m_ScanGen || (m_ScanCancel != null && m_ScanCancel.Requested);
            if (stale || job == null || job.Canceled || job.Result == null)
            {
                if (SkiResortSettings.DiagnosticsOn)
                    Mod.Log.Info(string.Format(CultureInfo.InvariantCulture,
                        "QuickRuns [probe] scan ({0}) dropped after {1:F0} ms on the worker ({2}): out of date", m_ScanJobWhy, m_ScanWorkerMs,
                        job != null && job.Canceled ? "canceled at " + (job.Progress * 100f).ToString("F0", CultureInfo.InvariantCulture) + " %" : "ran to the end"));
                if (m_PendingJob != null)
                    LaunchPending();
                return true;
            }
            if (job.Plan != null && !job.FromCache)
                CachePlan(m_ScanKey, job.Plan);
            m_DoneJob = job;
            m_Published = job.Result;
            FinishScan();
            AddScanMainMs(QuickRunsRules.MsSince(m_CollectTicks));
            return true;
        }


        // ---------------------------------------------------------------- the DONE line's fields

        private void ResetScanCounters()
        {
            m_ScanMainMs = m_ScanFrameMs = m_ScanFrameMaxMs = 0;
            m_ScanMsFrame = m_Frame;
        }

        /// <summary>Main-thread time of the scan, summed per frame for the worst frame (the tree job's fields, Trees.cs FinishTreeCompose).</summary>
        private void AddScanMainMs(double ms)
        {
            if (m_ScanMsFrame != m_Frame)
            {
                m_ScanMsFrame = m_Frame;
                m_ScanFrameMs = 0;
            }
            m_ScanFrameMs += ms;
            m_ScanMainMs += ms;
            m_ScanFrameMaxMs = Math.Max(m_ScanFrameMaxMs, m_ScanFrameMs);
        }

        /// <summary>"worker W ms (checks C, plan P or cached, realize R), main M ms over F frame(s) (worst frame X ms), gc0 +G": the
        /// collect frame so far is included.</summary>
        private string ScanJobText()
        {
            double collect = QuickRunsRules.MsSince(m_CollectTicks);
            double worst = m_ScanMsFrame == m_Frame ? m_ScanFrameMs + collect : collect;
            QuickRunsScanJob j = m_DoneJob;
            return string.Format(CultureInfo.InvariantCulture,
                "worker {0:F0} ms (checks {1:F0}, plan {2}, realize {3:F0}), main {4:F1} ms over {5} frame(s) (worst frame {6:F1} ms), gc0 +{7}",
                m_ScanWorkerMs, j?.PrecheckMs ?? 0.0, j?.Plan == null ? "none" : j.FromCache ? "cached" : j.Plan.PlanMs.ToString("F0", CultureInfo.InvariantCulture),
                j?.RealizeMs ?? 0.0, m_ScanMainMs + collect, m_ScanFrames, Math.Max(m_ScanFrameMaxMs, worst), GC.CollectionCount(0) - m_ScanGc0);
        }

        /// <summary>The Resort plan in one clause (spec 7 DONE line). "plan none" without a plan.</summary>
        private static string PlanText(ResortPlan p)
        {
            if (p == null)
                return "plan none";
            int proposed = 0, pinned = 0, clear = 0;
            var home = new int[6];
            for (int i = 0; i < p.Lifts.Length; i++)
            {
                if (p.Lifts[i].Origin == LiftOrigin.Pinned)
                    pinned++;
                else
                    proposed++;
                if (!p.Lifts[i].HandFit && !p.Lifts[i].TowersUnchecked)
                    clear++;
                int h = (int)p.Lifts[i].Home;
                if (h >= 0 && h < home.Length)
                    home[h]++;
            }
            return string.Format(CultureInfo.InvariantCulture,
                "groups {0}, lifts {1} (proposed {2}, pinned {3}, towers clear {12}), home G/B/K/DB/Download {4}/{5}/{6}/{7}/{8}, dead ends {9}, " +
                "run km {10:F2} ({11:F2} per km2)",
                p.Pods.Length, p.Lifts.Length, proposed, pinned, home[(int)HomeVerdict.Green], home[(int)HomeVerdict.Blue],
                home[(int)HomeVerdict.Black], home[(int)HomeVerdict.DoubleBlack], home[(int)HomeVerdict.Download], p.DeadEnds, p.RunKm,
                p.RunKmPerKm2, clear);
        }


        // ---------------------------------------------------------------- dev path (QuickRunsDevUISystem, Measurement diagnostics)

        /// <summary>Scan this ring (the dev trigger). The tool must be active. bare = no planner input (the host golden's input). The scan
        /// starts in the tool's own update (DevScanNow). Returns the reason when refused.</summary>
        internal string DevScan(float2[] ring, bool bare, string tag)
        {
            if (!IsActive)
                return "the tool is not active";
            if (ring == null || ring.Length < 3)
                return "fewer than three corners";
            m_DevRing = ring;
            m_DevBare = bare;
            m_DevTag = tag;
            return "queued";
        }

        /// <summary>The queued dev scan, in the tool's update: the corners replace the region, the region closes, the scan starts.</summary>
        private void DevScanNow()
        {
            float2[] ring = m_DevRing;
            m_DevRing = null;
            if (Building || m_Planting || m_Phase == ToolPhase.Emitted || m_Phase == ToolPhase.Retry || m_Phase == ToolPhase.Applying)
            {
                Mod.Log.Warn("QuickRuns [dev]: scan REFUSED — a build or a planting runs");
                return;
            }
            if (m_TreesOn)
                SetTrees(false, "dev scan");
            if (m_Phase == ToolPhase.Scanning || m_Phase == ToolPhase.Preview)
                DropPreview("dev scan: the region is replaced");
            m_Edit.Clear();
            for (int i = 0; i < ring.Length; i++)
                m_Edit.Add(ring[i]);
            ClearPlanToggles();
            m_RegionClosed = true;
            m_NextBare = m_DevBare;
            m_NextTag = string.IsNullOrEmpty(m_DevTag) ? "region" : m_DevTag;
            UpdateReadout();
            Say($"QuickRuns [dev]: scan {m_NextTag}{(m_NextBare ? " (bare: no planner input)" : string.Empty)} — {ring.Length} corner(s); {ReadoutText()}");
            BeginScan("dev " + m_NextTag);
        }

        /// <summary>Writes the last published scan's exact input and its hashes to the log folder (QuickRunsScanDump), for the test host's
        /// rf-replay. stem = a file name stem (letters, digits, dash), null = "scan". Returns the log line.</summary>
        internal string DevDump(string stem)
        {
            QuickRunsScanJob job = m_DoneJob;
            if (job == null || job.Input == null)
                return "nothing to dump: no scan was published since the tool opened";
            if (ScanJobInFlight)
                return "a scan runs: dump after it ends";
            if (string.IsNullOrEmpty(stem))
                stem = "scan";
            foreach (char c in stem)
                if (!char.IsLetterOrDigit(c) && c != '-')
                    return $"'{stem}' is not a plain file stem (letters, digits, dash)";
            string dir = Path.GetDirectoryName(SkiDiagnostics.LogFilePath);
            string path = Path.Combine(dir, "SkiResortMod." + stem + ".rfscan");
            var head = new QuickRunsScanDump.Header
            {
                Tag = job.Id, Bare = job.PlannerInputs == null, PlanHash = job.Plan?.Hash ?? 0UL, ResultHash = job.Result?.Fingerprint ?? 0UL,
                OptionsPrint = job.Options.Fingerprint(), Status = job.Result?.Status.ToString() ?? "none",
            };
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                QuickRunsScanDump.Write(fs, head, job.Input, job.Options);
            return string.Format(CultureInfo.InvariantCulture,
                "dump of {0} written to {1} ({2:F1} MB, {3:F0} ms): plan {4:x16}, result {5:x16}, options {6:x16}, status {7}; replay: rf-replay <file>",
                head.Tag, Mod.ScrubUserPath(path), new FileInfo(path).Length / 1048576.0, QuickRunsRules.MsSince(t0), head.PlanHash, head.ResultHash,
                head.OptionsPrint, head.Status);
        }


        // ---------------------------------------------------------------- the plan context (main thread, before the launch)

        /// <summary>Pinned lifts, access points and the tree line into <paramref name="input"/>, all sorted (spec 4.7). Main thread: census
        /// copies, the net search tree, the sign lookup. Measured as m_ContextMs (budget 2 ms, spec 5.3).</summary>
        private void CollectPlanContext(AutoPlanInput input, float2[] region)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            input.Pinned = CollectPinned(region);
            input.Access = CollectAccess(region);
            input.TreeLineY = !m_TreeLineStale && m_TreeLineEst.Source != TreeLineSource.None && math.isfinite(m_TreeLineEst.Height)
                ? m_TreeLineEst.Height
                : float.NaN;
            m_ContextMs = QuickRunsRules.MsSince(t0);
        }

        /// <summary>Census lifts with a chord within PinnedReachM of the region (LiftCensusSystem.CopyServeLifts, row order = CopyRows).
        /// Bottom / Top = the stations' stub tips (NaN when that station is not in the census's last build). Key = FNV of the row's
        /// EntityRef. Null when none.</summary>
        private List<PinnedLift> CollectPinned(float2[] region)
        {
            if (m_LiftCensus == null)
                m_LiftCensus = World.GetExistingSystemManaged<LiftCensusSystem>();
            if (m_LiftCensus == null || !m_LiftCensus.IsArmed)
                return null;
            int n = m_LiftCensus.CopyServeLifts(m_ServeLifts, m_ServeChords);
            m_LiftCensus.CopyRows(m_CensusRows);
            if (n == 0)
                return null;
            if (m_CensusRows.Count != n)
            {
                Mod.Log.Warn($"QuickRuns [scan]: census rows {m_CensusRows.Count} != serve lifts {n} — no pinned lifts this scan");
                return null;
            }
            m_StubTips.Clear();
            for (int i = 0; i < m_LiftCensus.StationCount; i++)
            {
                LiftCensus.Station st = m_LiftCensus.StationAt(i);
                m_StubTips[st.Id] = st.StubTip;
            }
            List<PinnedLift> pinned = null;
            var nan = new float3(float.NaN, float.NaN, float.NaN);
            for (int r = 0; r < n; r++)
            {
                LiftServedRule.Lift lift = m_ServeLifts[r];
                if (lift.ChordCount <= 0 || !NearRegion(region, lift.FirstChord, lift.ChordCount))
                    continue;
                var chords = new float2[lift.ChordCount * 2];
                for (int c = 0; c < lift.ChordCount; c++)
                {
                    chords[2 * c] = m_ServeChords[lift.FirstChord + c].A;
                    chords[2 * c + 1] = m_ServeChords[lift.FirstChord + c].B;
                }
                pinned = pinned ?? new List<PinnedLift>();
                pinned.Add(new PinnedLift
                {
                    Key = KeyOf(lift.Id),
                    TypeIndex = m_CensusRows[r].Type,
                    Bottom = !lift.Base.IsNull && m_StubTips.TryGetValue(lift.Base, out float3 b) ? b : nan,
                    Top = !lift.Top.IsNull && m_StubTips.TryGetValue(lift.Top, out float3 t) ? t : nan,
                    Chords = chords,
                    Complete = lift.Complete,
                });
            }
            pinned?.Sort((a, b) =>
            {
                int k = a.Key.CompareTo(b.Key);
                if (k != 0)
                    return k;
                k = a.Bottom.x.CompareTo(b.Bottom.x);
                return k != 0 ? k : a.Bottom.z.CompareTo(b.Bottom.z);
            });
            return pinned;
        }

        /// <summary>A chord sample (every PinnedSampleM) inside the region or within PinnedReachM of its outline.</summary>
        private bool NearRegion(float2[] region, int first, int count)
        {
            for (int c = first; c < first + count; c++)
            {
                float2 a = m_ServeChords[c].A, b = m_ServeChords[c].B;
                int steps = math.max(1, (int)math.ceil(math.distance(a, b) / PinnedSampleM));
                for (int k = 0; k <= steps; k++)
                {
                    float2 p = math.lerp(a, b, k / (float)steps);
                    if (GradeRaster.Inside(region, p) || PlanGeometry.PointRingDistance(region, p) <= PinnedReachM)
                        return true;
                }
            }
            return false;
        }

        private static uint KeyOf(EntityRef id)
        {
            uint h = 2166136261u;
            h = (h ^ (uint)id.Index) * 16777619u;
            h = (h ^ (uint)id.Version) * 16777619u;
            return h;
        }

        /// <summary>Road edge samples (both ends of each live road edge plus every AccessStepM along its curve, the step widened so the
        /// interior samples stay within AccessMaxSamples over all edges) and the nearest Welcome Sign. They lie within the region's bounds
        /// plus AccessReachM. The list is sorted by x then z, with duplicates (shared nodes) removed. Null when none. The sign: the nearest
        /// one to the bounds center only (SkiRunOwnershipSystem.TryNearestGateEntity), when it lies within the reach.</summary>
        private float2[] CollectAccess(float2[] region)
        {
            const int AccessMaxSamples = 2048;   // a dense road grid by a 9 km2 region gave 6-12 k samples at 25 m (A-R AR-8)
            if (!PlanGeometry.BBox(region, out float2 lo, out float2 hi))
                return null;
            var box = new Bounds2(lo - AccessReachM, hi + AccessReachM);
            var points = new List<float2>();
            var curves = new List<Game.Net.Curve>();
            if (m_NetSearch == null)
                m_NetSearch = World.GetOrCreateSystemManaged<Game.Net.SearchSystem>();
            // Synchronous read (the glade tool's CensusTrees idiom, as SearchTrees does for the object tree)
            NativeQuadTree<Entity, QuadTreeBoundsXZ> tree = m_NetSearch.GetNetSearchTree(readOnly: true, out JobHandle deps);
            deps.Complete();
            var found = new NativeList<Entity>(256, Allocator.Temp);
            try
            {
                var it = new NetBoundsIterator { m_Bounds = box, m_Results = found };
                tree.Iterate(ref it);
                for (int i = 0; i < found.Length; i++)
                {
                    Entity e = found[i];
                    if (!EntityManager.HasComponent<Game.Net.Road>(e) || !EntityManager.HasComponent<Game.Net.Edge>(e) ||
                        EntityManager.HasComponent<Deleted>(e) || EntityManager.HasComponent<Temp>(e) ||
                        !EntityManager.TryGetComponent(e, out Game.Net.Curve curve))
                        continue;
                    curves.Add(curve);
                }
            }
            finally
            {
                found.Dispose();
            }
            // The step from the total length (the edge set, not the iteration order, decides it): ends always, interior samples capped
            double total = 0.0;
            for (int i = 0; i < curves.Count; i++)
                total += curves[i].m_Length;
            float step = (float)Math.Max(AccessStepM, total / AccessMaxSamples);
            for (int i = 0; i < curves.Count; i++)
            {
                int steps = math.max(1, (int)math.ceil(curves[i].m_Length / step));
                for (int k = 0; k <= steps; k++)
                {
                    float2 p = MathUtils.Position(curves[i].m_Bezier, k / (float)steps).xz;
                    if (MathUtils.Intersect(box, p))
                        points.Add(p);
                }
            }
            if (m_Ownership.TryNearestGateEntity((lo + hi) * 0.5f, out Entity gate, out float _) &&
                EntityManager.TryGetComponent(gate, out Game.Objects.Transform tr) && MathUtils.Intersect(box, tr.m_Position.xz))
                points.Add(tr.m_Position.xz);
            if (points.Count == 0)
                return null;
            points.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            int m = 0;
            for (int i = 0; i < points.Count; i++)
                if (m == 0 || !points[i].Equals(points[m - 1]))
                    points[m++] = points[i];
            points.RemoveRange(m, points.Count - m);
            return points.ToArray();
        }

        private struct NetBoundsIterator : INativeQuadTreeIterator<Entity, QuadTreeBoundsXZ>, IUnsafeQuadTreeIterator<Entity, QuadTreeBoundsXZ>
        {
            public Bounds2 m_Bounds;
            public NativeList<Entity> m_Results;

            public bool Intersect(QuadTreeBoundsXZ bounds) => MathUtils.Intersect(bounds.m_Bounds.xz, m_Bounds);

            public void Iterate(QuadTreeBoundsXZ bounds, Entity entity)
            {
                if (Intersect(bounds))
                    m_Results.Add(entity);
            }
        }
    }
}
