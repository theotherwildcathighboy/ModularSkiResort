// Quick runs tool: the scan job (Resort Planner v2 stage A, task A2; stage-a-spec.md section 5). The tree filler's compose job is the
// pattern (QuickRunsToolSystem.Trees.cs: state, launch bookkeeping with a GC count, stale test, per-frame poll and cancel, private
// copies and Task.Run, collect with IsFaulted and the stale drop, the summary fields). States: Idle -> Running (a worker thread, or
// main-thread slices with the Diagnostics fallback "Resort Planner on the main thread") -> collected on the main thread -> Idle.
// The worker touches ONLY the planner instance and the input it was given: a fresh AutoPlanInput per scan, a fresh obstacle list,
// and a snapshot buffer that is never rewritten while the planner holds it (TakeSnapshot writes the other one). No Entities, no Unity
// API and no log on the worker. The result reaches every reader as an immutable copy (m_Published = Result.Snapshot()), so a Reselect
// or a new scan on the worker never changes what the overlay, the hub, the tree filler or a build reads. One job at a time: a scan
// requested while a canceled job is still stopping is queued and launched when that job returns (cancel latency = one planner unit).
// Build, Undo and Plant never overlap a running job: Build needs the Preview phase, and the hub refuses build / undo / plant / trees
// while Scanning (ScanBlocks). Pinned lifts and access points are collected here on the main thread before the launch.
// Logs (silence impossible): START / DONE (Scan.cs), CANCELED (here, every cancel of a running or queued scan), `!!!` on a faulted
// job (Fail: the tool is disabled until the next load, the v1 policy); the drop of a stale result only with Measurement diagnostics.
// Engine notes: EngineNotes/QuickRunsToolSystem.md (Scan, Worker)

using System;
using System.Collections.Generic;
using System.Globalization;
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

        // The published result: an immutable copy every reader takes (never the planner's live Result, which the worker rewrites)
        private AutoPlanResult m_Published = NoResult();
        private Task m_ScanTask;                 // the worker job; null = none in flight
        private AutoPlanCancel m_ScanCancel;     // the planner's cancel token for the current input (one per launched scan)
        private int m_ScanGen, m_ScanLaunchGen;  // stale test: any cancel or new request bumps m_ScanGen
        private bool m_ScanSlicing;              // a main-thread scan (fallback setting) is being stepped
        private bool m_ScanOnMain;               // the thread of the last launched job
        private AutoPlanInput m_PendingInput;    // a scan requested while a canceled job was still stopping
        private bool m_PendingOnMain;
        private string m_ScanJobWhy;             // the launched job's reason (m_ScanWhy may already name the queued one)
        private long m_ScanLaunchTicks, m_CollectTicks;
        private int m_ScanGc0, m_ScanMsFrame;
        private double m_ScanWorkerMs, m_ScanMainMs, m_ScanFrameMs, m_ScanFrameMaxMs, m_ContextMs;
        private LiftCensusSystem m_LiftCensus;
        private Game.Net.SearchSystem m_NetSearch;
        private readonly List<LiftServedRule.Lift> m_ServeLifts = new List<LiftServedRule.Lift>();
        private readonly List<LiftServedRule.Chord> m_ServeChords = new List<LiftServedRule.Chord>();
        private readonly List<LiftCensus.Row> m_CensusRows = new List<LiftCensus.Row>();
        private readonly Dictionary<EntityRef, float3> m_StubTips = new Dictionary<EntityRef, float3>();

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


        // ---------------------------------------------------------------- launch

        /// <summary>A new scan of <paramref name="input"/> (BeginScan): launched now, or queued behind a canceled job still stopping.</summary>
        private void StartScanJob(AutoPlanInput input, bool onMain, long t0)
        {
            m_ScanGen++;
            ResetScanCounters();
            if (ScanJobInFlight)
            {
                m_ScanCancel?.Request();   // normally requested already (the drop that preceded this scan)
                m_PendingInput = input;
                m_PendingOnMain = onMain;
            }
            else
            {
                m_PendingInput = null;
                // BeginScan polled the job before its snapshot and obstacles, which take tens of ms on a large region: a worker that
                // finished since is collected here, so a fault is reported (Fail) instead of overwritten unseen. Its generation is
                // already old (m_ScanGen++ above), so a clean finish is dropped as out of date and nothing pending is launched.
                if (!PollScanJob(true))
                    return;
                LaunchScan(input, onMain, false);
            }
            AddScanMainMs(QuickRunsRules.MsSince(t0));
        }

        /// <summary>The job itself. resume = the planner was Reselected on its cached field: step it to the end instead of Run(input).</summary>
        private void LaunchScan(AutoPlanInput input, bool onMain, bool resume)
        {
            m_ScanSlicing = false;
            if (!resume)
            {
                m_ScanCancel = new AutoPlanCancel();
                input.Cancel = m_ScanCancel;
                m_Input = input;
                m_PlannerSnap = input.Snapshot;   // TakeSnapshot now writes the other buffer
            }
            m_ScanLaunchGen = m_ScanGen;
            m_ScanOnMain = onMain;
            m_ScanJobWhy = m_ScanWhy;
            m_ScanLaunchTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            m_ScanGc0 = GC.CollectionCount(0);
            m_ScanWorkerMs = 0;
            if (onMain)
            {
                // The v1 path: Begin here (cheap refusals are final at once), then ScanBudgetMs per frame in StepScanJob
                if (resume || m_Planner.Begin(input) == AutoPlanStatus.Running)
                    m_ScanSlicing = true;
                else
                    CollectScanJob(false);
                return;
            }
            SkiRunAutoPlanner planner = m_Planner;
            m_ScanTask = Task.Run(() =>
            {
                long w0 = System.Diagnostics.Stopwatch.GetTimestamp();
                if (resume)
                {
                    while (!planner.Step(float.PositiveInfinity))
                    {
                    }
                }
                else
                    planner.Run(input);
                m_ScanWorkerMs = QuickRunsRules.MsSince(w0);   // read after IsCompleted (the task's completion orders it)
            });
        }

        private void LaunchPending()
        {
            AutoPlanInput input = m_PendingInput;
            m_PendingInput = null;
            if (SkiResortSettings.DiagnosticsOn)
                Mod.Log.Info($"QuickRuns [probe] scan ({m_ScanWhy}) launched after the canceled scan stopped");
            LaunchScan(input, m_PendingOnMain, false);
        }

        /// <summary>A knob changed in Preview or Scanning (SetKnobs). No job running (a worker job in flight or a main-thread scan being stepped both count) and the planner's field intact: Reselect and resume
        /// on the worker (Reselect only sets the restart stage). Else a running job is canceled first and the region scans again with the
        /// new knobs (a canceled planner drops its field: Reselect refuses, SkiRunAutoPlanner.CancelScan).</summary>
        private void RescanForKnobs(string name)
        {
            if (m_Disabled || !PollScanJob(true))
                return;
            if (!ScanJobInFlight && !m_ScanSlicing && m_PendingInput == null && m_ScanCancel != null && !m_ScanCancel.Requested && m_Planner.Reselect(m_Knobs))
            {
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                bool onMain = SkiResortSettings.PlannerOnMainThreadOn;
                m_ScanWhy = name;
                m_ScanT0 = t0;
                m_ScanFrames = 0;
                m_Refused.Clear();
                m_Phase = ToolPhase.Scanning;
                m_ScanGen++;
                ResetScanCounters();
                Say($"QuickRuns [scan]: START ({name}) — Reselect on the cached field, {KnobsText(m_Knobs)}; " +
                    (onMain ? "main thread (fallback setting)" : "worker thread"));
                LaunchScan(m_Input, onMain, true);
                AddScanMainMs(QuickRunsRules.MsSince(t0));
                Bump();
                return;
            }
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
            if (m_ScanSlicing)
            {
                bool done = m_Planner.Step(ScanBudgetMs);
                AddScanMainMs(QuickRunsRules.MsSince(t0));
                if (!done)
                    return false;
                m_ScanSlicing = false;
                CollectScanJob(true);
                return true;
            }
            if (m_PendingInput != null)
            {
                LaunchPending();
                return false;
            }
            Mod.Log.Warn("QuickRuns [scan]: phase Scanning without a job (contract drift?) — back to the region");
            m_Phase = ToolPhase.Region;
            Bump();
            return true;
        }

        /// <summary>Collects a finished job outside the per-frame poll (before a new scan or a Reselect). False when it had faulted.</summary>
        private bool PollScanJob(bool canClose)
        {
            if (m_ScanTask == null || !m_ScanTask.IsCompleted)
                return true;
            return CollectScanJob(canClose);
        }

        /// <summary>Stops the running or queued scan: the worker stops at its next unit and its result is dropped when it returns; a
        /// main-thread scan stops now. Every cancel of something running writes one CANCELED line.</summary>
        private void CancelScanJob(string reason)
        {
            m_ScanGen++;
            if (m_PendingInput != null)
            {
                m_PendingInput = null;
                Mod.Log.Info($"QuickRuns [scan]: CANCELED ({m_ScanWhy}, queued) — {reason}; it never started");
            }
            bool worker = ScanJobInFlight && m_ScanCancel != null && !m_ScanCancel.Requested;
            if (!worker && !m_ScanSlicing)
                return;
            m_ScanCancel?.Request();
            if (m_ScanSlicing)
            {
                m_Planner.Step(0f);   // sees the request before any unit: the planner is canceled and reusable now
                m_ScanSlicing = false;
            }
            Mod.Log.Info(string.Format(CultureInfo.InvariantCulture,
                "QuickRuns [scan]: CANCELED ({0}) — {1}; {2:F0} ms on the {3} so far, progress {4:P0}{5}",
                m_ScanJobWhy, reason, QuickRunsRules.MsSince(m_ScanLaunchTicks), worker ? "worker" : "main thread",
                m_Planner.ProgressVolatile, worker ? "; the worker stops at its next unit, its result is dropped" : string.Empty));
        }

        /// <summary>Main thread, the job is done: a fault disables the tool (the v1 Fail policy); a stale or canceled result is dropped
        /// (and a queued scan launches); else the result is published and the v1 epilogue runs (FinishScan). False after a fault.</summary>
        private bool CollectScanJob(bool canClose)
        {
            m_CollectTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            Task task = m_ScanTask;
            m_ScanTask = null;
            if (task != null && task.IsFaulted)
            {
                m_PendingInput = null;
                m_ScanCancel?.Request();   // the planner's field is suspect: Reselect must not reuse it
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
            AutoPlanResult live = m_Planner.Result;
            bool stale = m_ScanLaunchGen != m_ScanGen || (m_ScanCancel != null && m_ScanCancel.Requested);
            if (stale || live.Canceled)
            {
                if (SkiResortSettings.DiagnosticsOn)
                    Mod.Log.Info(string.Format(CultureInfo.InvariantCulture,
                        "QuickRuns [probe] scan ({0}) dropped after {1:F0} ms on the {2} ({3}): out of date", m_ScanJobWhy,
                        m_ScanOnMain ? QuickRunsRules.MsSince(m_ScanLaunchTicks) : m_ScanWorkerMs, m_ScanOnMain ? "main thread" : "worker",
                        live.Canceled ? "canceled after " + live.Units + " unit(s)" : "ran to the end"));
                if (m_PendingInput != null)
                    LaunchPending();
                return true;
            }
            m_Published = live.Snapshot();
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

        /// <summary>"worker W ms, main M ms over F frame(s) (worst frame X ms), gc0 +G": the collect frame so far is included.</summary>
        private string ScanJobText()
        {
            double collect = QuickRunsRules.MsSince(m_CollectTicks);
            double worst = m_ScanMsFrame == m_Frame ? m_ScanFrameMs + collect : collect;
            return string.Format(CultureInfo.InvariantCulture,
                "{0} {1}, main {2:F1} ms over {3} frame(s) (worst frame {4:F1} ms), gc0 +{5}",
                m_ScanOnMain ? "main thread (fallback setting):" : "worker",
                m_ScanOnMain ? "no worker" : m_ScanWorkerMs.ToString("F0", CultureInfo.InvariantCulture) + " ms",
                m_ScanMainMs + collect, m_ScanFrames, Math.Max(m_ScanFrameMaxMs, worst), GC.CollectionCount(0) - m_ScanGc0);
        }

        /// <summary>The Resort plan in one clause (spec 7 DONE line); "plan none" in Runs mode.</summary>
        private static string PlanText(ResortPlan p)
        {
            if (p == null)
                return "plan none";
            int proposed = 0, pinned = 0;
            var home = new int[6];
            for (int i = 0; i < p.Lifts.Length; i++)
            {
                if (p.Lifts[i].Origin == LiftOrigin.Pinned)
                    pinned++;
                else
                    proposed++;
                int h = (int)p.Lifts[i].Home;
                if (h >= 0 && h < home.Length)
                    home[h]++;
            }
            return string.Format(CultureInfo.InvariantCulture,
                "pods {0}, lifts {1} (proposed {2}, pinned {3}), home G/B/K/DB/Download {4}/{5}/{6}/{7}/{8}, dead ends {9}, " +
                "run km {10:F2} ({11:F2} per km2)",
                p.Pods.Length, p.Lifts.Length, proposed, pinned, home[(int)HomeVerdict.Green], home[(int)HomeVerdict.Blue],
                home[(int)HomeVerdict.Black], home[(int)HomeVerdict.DoubleBlack], home[(int)HomeVerdict.Download], p.DeadEnds, p.RunKm,
                p.RunKmPerKm2);
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
        /// Bottom / Top = the stations' stub tips (NaN when that station is not in the census's last build); Key = FNV of the row's
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
                    if (GradeRaster.Inside(region, p) || SkiRunAutoPlanner.PointRingDistance(region, p) <= PinnedReachM)
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
        /// interior samples stay within AccessMaxSamples over all edges) and the nearest Welcome Sign, within the region's bounds plus
        /// AccessReachM, sorted by x then z, duplicates (shared nodes) removed. Null when none. The sign: the nearest one to the bounds
        /// center only (SkiRunOwnershipSystem.TryNearestGateEntity), when it lies within the reach.</summary>
        private float2[] CollectAccess(float2[] region)
        {
            const int AccessMaxSamples = 2048;   // a dense road grid by a 9 km2 region gave 6-12 k samples at 25 m (A-R AR-8)
            if (!SkiRunAutoPlanner.BBox(region, out float2 lo, out float2 hi))
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
