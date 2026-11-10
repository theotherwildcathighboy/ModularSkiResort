// Quick runs tool: the scan. It copies the region's texel rectangle of the CPU heights (plus a wet layer from the water surface) into the
// back buffer of two HeightSnapshots once. It collects the obstacles (every Surface area, our runs, unowned map tiles), the existing lifts
// and the access points. It gives a fresh input to the scan job (QuickRunsToolSystem.Worker.cs, QuickRunsScanJob: the runs-first planner
// and the realizer on a worker thread). The planner options come from the Density knob (RunsFirstOptions.ForDensity). The planner's
// blocked ground comes from the same input (QuickRunsScanJob.InputsFrom). Every reader takes the published result (m_Published). On Enter
// the snapshot and the obstacles are read again: a changed world re-scans instead of building a stale plan. Warnings: the realizer's, then
// the tool's (money, renumbering, other resort, no sign, game checks off).
// Engine notes: EngineNotes/QuickRunsToolSystem.md (Scan)

using System;
using System.Collections.Generic;
using System.Globalization;
using Colossal.Entities;
using Colossal.Mathematics;
using Game.Areas;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using SkiResortMod.Settings;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class QuickRunsToolSystem
    {
        private const float WetDepthM = 0.1f;   // the cat track tool's threshold

        private AutoPlanInput m_Input = new AutoPlanInput();                // the last launched scan's input (fresh per launch)
        private List<AutoObstacle> m_Obstacles = new List<AutoObstacle>();    // fresh per collect: a running job reads the old list
        private HeightSnapshot m_Snap;                                        // the latest snapshot taken (one of m_SnapBuf)
        private readonly HeightSnapshot[] m_SnapBuf = new HeightSnapshot[2];
        private HeightSnapshot m_PlannerSnap;                                 // the buffer the planner holds (never rewritten)
        private ulong m_SnapPrint, m_ObstaclePrint;
        private AutoPlanKnobs m_Knobs = AutoPlanKnobs.Default(0);
        private uint m_Shuffle;
        private string m_ScanWhy;
        private long m_ScanT0;
        private int m_ScanFrames;
        private double m_SnapMs, m_ObstacleMs;
        private readonly HashSet<int> m_OffIds = new HashSet<int>();      // candidate ids the player left out (AutoRun.Id is stable)
        private readonly HashSet<int> m_Refused = new HashSet<int>();     // candidate ids the game refused on the last build
        private bool[] m_EnabledByIndex = new bool[0];
        private readonly QuickTotals m_Totals = new QuickTotals();
        private readonly List<string> m_WarnKeys = new List<string>();
        private QuickToolWarning m_ToolWarn;
        private string m_StatusKey;
        // Gates of the enabled candidates (OtherResort / NoSign), by result index
        private Entity[] m_GateOf = new Entity[0];

        internal bool IsEnabled(int i) => i >= 0 && i < m_EnabledByIndex.Length && m_EnabledByIndex[i];
        internal bool IsRefused(AutoRun r) => r != null && m_Refused.Contains(r.Id);


        private void BeginScan(string why)
        {
            if (m_Phase != ToolPhase.Region && m_Phase != ToolPhase.Preview)
                return;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            if (m_Disabled || !PollScanJob(true))
                return;   // a faulted job disabled the tool
            float2[] region = CornerArray();
            UpdateReadout();
            if (region == null || region.Length < 3)
            {
                Say("QuickRuns [scan]: nothing to scan — click at least three corners", NoticeKey("NEED_CORNERS"));
                return;
            }
            m_RegionClosed = true;
            if (m_Readout.State == QuickRegionState.Over)
            {
                Say($"QuickRuns [scan]: REFUSED — {ReadoutText()}", QuickRunsRules.StatusKey(AutoPlanStatus.TooLarge));
                return;
            }
            if (m_TreesOn)
                SetTrees(false, "scan");
            if (!TakeSnapshot(region, out string failure))
            {
                Say($"QuickRuns [scan]: REFUSED — {failure}", QuickRunsRules.StatusKey(AutoPlanStatus.BadInput));
                return;
            }
            CollectObstacles(region);
            m_Knobs.Seed = PlanGeometry.SeedOf(region, m_Shuffle);
            // A fresh input per scan (the job holds it until the next launch): Resort mode, the realizer's input contract
            var input = new AutoPlanInput
            {
                Snapshot = m_Snap,
                Region = region,
                Obstacles = m_Obstacles,
                Knobs = m_Knobs,
                Mode = AutoPlanMode.Resort,
                Cancel = new AutoPlanCancel(),
            };
            CollectPlanContext(input, region);
            bool bare = m_NextBare;
            string tag = m_NextTag;
            m_NextBare = false;
            m_NextTag = "region";
            RunsFirstOptions options = RunsFirstOptions.ForDensity(m_Knobs.Density);
            RunsFirstInputs inputs = QuickRunsScanJob.InputsFrom(input, bare);
            ulong key = QuickRunsScanJob.PlanKey(m_SnapPrint, m_ObstaclePrint, region, input.Pinned, input.Access, options, bare);
            QuickRunsScanJob.Planned cached = CachedPlan(key);
            var job = new QuickRunsScanJob(input, options, inputs, cached, tag);
            m_Refused.Clear();
            m_ScanWhy = why;
            m_ScanT0 = t0;
            m_ScanFrames = 0;
            Say(string.Format(CultureInfo.InvariantCulture,
                "QuickRuns [scan]: START ({0}) — {1}; snapshot {2} x {3} texels ({4:F1} ms), {5} obstacle(s) ({6:F1} ms), {7}; " +
                "{8} existing lift(s), {9} access point(s) ({10:F1} ms); runs-first on the worker thread, {11}, {12}{13}",
                why, ReadoutText(), m_Snap.Width, m_Snap.Depth, m_SnapMs, m_Obstacles.Count, m_ObstacleMs, KnobsText(m_Knobs),
                input.Pinned?.Count ?? 0, input.Access?.Length ?? 0, m_ContextMs, QuickRunsScanJob.InputsText(input, inputs),
                cached != null ? "cached plan " + cached.Hash.ToString("x16", CultureInfo.InvariantCulture) + ": realize only" : "a new plan",
                ScanJobInFlight ? " (queued: the canceled scan is still stopping)" : string.Empty));
            if (input.Pinned != null && input.Pinned.Count > 0)
                Say($"QuickRuns [scan]: {input.Pinned.Count} existing lift(s) are blocked ground for the planner (stations and a {QuickRunsScanJob.ExistingLiftCorridorM:F0} m corridor each): runs-first has no fixed lifts, so it plans around them");
            m_Phase = ToolPhase.Scanning;
            Bump();
            StartScanJob(job, key, t0);
        }

        /// <summary>Per frame while Scanning: the worker poll.</summary>
        private void StepScan()
        {
            m_ScanFrames++;
            if (!StepScanJob() && (m_ScanFrames & 7) == 0)
                Bump();   // the hub's progress bar
        }

        /// <summary>The published result's epilogue: phase, enabled flags, the plan sheet, the DONE line (and the [probe] lines). The caller
        /// has just set m_Published and m_DoneJob.</summary>
        private void FinishScan()
        {
            AutoPlanResult r = m_Published;
            QuickRunsScanJob job = m_DoneJob;
            double ms = QuickRunsRules.MsSince(m_ScanT0);
            m_StatusKey = QuickRunsRules.StatusKey(r.Status, m_Obstacles != null && m_Obstacles.Exists(o => o.Kind == AutoObstacleKind.Unowned));
            m_Phase = r.Status == AutoPlanStatus.Ok ? ToolPhase.Preview : ToolPhase.Region;
            RefreshEnabled();
            PublishSheet($"scan ({m_ScanWhy})");   // the plan sheet takes the proposed lifts (Plan.cs)
            if (r.OpenShare > 0f)
                m_SiteGrade = r.MeanGrade;   // the scan's figure (the realizer's lattice over the open ground) replaces the corner phase's estimate
            int tracks = 0;
            double pisteKm = 0.0, trackKm = 0.0;
            foreach (AutoRun run in r.Runs)
            {
                if (RunFeatures.FromKind((int)run.Kind).Base == RunBase.CatTrack)
                {
                    tracks++;
                    trackKm += run.LengthM / 1000.0;
                }
                else
                    pisteKm += run.LengthM / 1000.0;
            }
            string plan = job?.Plan == null ? "no plan (" + (job != null && job.Refused ? "region refused before planning" : "none") + ")"
                : string.Format(CultureInfo.InvariantCulture, "plan {0:x16} {1}", job.Plan.Hash, job.FromCache ? "(cached)" : "(new)");
            LastScanText = string.Format(CultureInfo.InvariantCulture,
                "{0}: {1} area(s) ({2} piste(s) {3:F1} km, {4} cat track(s) {5:F1} km) in {6} group(s), bands G/B/K/DB {7}/{8}/{9}/{10}, " +
                "{11} planner element(s); open {12:P0}, usable {13:P0}, cliff {14:P0}, vertical {15:F0} m, mean grade {16:P0}; {17}; " +
                "{18:F0} ms over {19} frame(s), area {20:F2} km2, rejects {{{21}}}",
                r.Status, r.Runs.Count, r.Runs.Count - tracks, pisteKm, tracks, trackKm, r.Clusters.Count, r.BandCount[1], r.BandCount[2], r.BandCount[3],
                r.BandCount[4], r.Candidates, r.OpenShare, r.UsableShare, r.CliffShare, r.VerticalM, r.MeanGrade, plan, ms, m_ScanFrames,
                r.RegionAreaM2 / 1e6f, r.RejectText());
            Say($"QuickRuns [scan]: DONE ({m_ScanWhy}) — {LastScanText}; warnings {r.Warnings}; tool warnings {m_ToolWarn}; " +
                $"{ScanJobText()}; {PlanText(r.Resort)}",
                m_StatusKey);
            if (SkiResortSettings.DiagnosticsOn)
                ProbeScan(job, ms);
            Bump();
        }

        /// <summary>Leave the preview: Region phase. The planner keeps its result and cache (Reselect may restart from them). Every reader
        /// of the result checks the phase first.</summary>
        private void DropPreview(string why)
        {
            if (m_Phase == ToolPhase.Scanning)
                CancelScanJob(why ?? "preview dropped");   // a running job stops at its next unit. Its result is dropped
            PollScanJob(false);   // a job that already returned: dropped now, or its fault logged (silence impossible)
            if (m_Phase == ToolPhase.Preview || m_Phase == ToolPhase.Scanning)
                m_Phase = ToolPhase.Region;
            m_EnabledByIndex = new bool[0];
            m_Totals.Clear();
            m_WarnKeys.Clear();
            m_ToolWarn = QuickToolWarning.None;
            m_StatusKey = null;
            m_DrawDirty = true;
            if (why != null)
            {
                Say($"QuickRuns [back]: {why}");
                Bump();
            }
        }


        /// <summary>Copies the texel rectangle of the region (PlanGeometry.SnapshotRect) from the CPU heights, and the wet layer
        /// (WaterUtils.SampleDepth over WetDepthM at each texel center). Main thread. The vanilla precedent for reading hd.heights outside
        /// a job is SelectionToolSystem.cs:1151 (the glade tool's note).</summary>
        private bool TakeSnapshot(float2[] region, out string failure)
        {
            failure = null;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            TerrainHeightData hd = m_TerrainSystem.GetHeightData();
            if (!hd.isCreated)
            {
                failure = "the terrain is not ready";
                return false;
            }
            int resX = hd.resolution.x, resZ = hd.resolution.z;
            if (!PlanGeometry.SnapshotRect(region, resX, resZ, hd.scale, hd.offset, out int x0, out int z0, out int w, out int d))
            {
                failure = "the region is degenerate";
                return false;
            }
            if ((long)w * d > PlanGeometry.MaxLatticeCells * 4L)
            {
                failure = $"the scan rectangle is {w} x {d} texels";
                return false;
            }
            // The back buffer: never the one the planner holds (a running worker reads it, a Reselect reads it again)
            int slot = m_PlannerSnap != null && ReferenceEquals(m_SnapBuf[0], m_PlannerSnap) ? 1 : 0;
            HeightSnapshot back = m_SnapBuf[slot];
            HeightSnapshot s = back != null && back.Heights.Length >= w * d && back.Wet != null && back.Wet.Length >= w * d
                ? Reuse(back, x0, z0, w, d, resX, resZ, hd.scale, hd.offset)
                : HeightSnapshot.Allocate(x0, z0, w, d, resX, resZ, hd.scale, hd.offset, wet: true);
            m_SnapBuf[slot] = s;
            NativeArray<ushort> heights = hd.heights;
            ulong h = 14695981039346656037UL;
            ushort[] sh = s.Heights;
            for (int l = 0; l < d; l++)
            {
                // One row copy (memcpy), then the print over the managed row (P2: the 9 km2 cap's ~800k texels in one frame)
                int dst = l * w;
                NativeArray<ushort>.Copy(heights, (z0 + l) * resX + x0, sh, dst, w);
                for (int k = dst; k < dst + w; k++)
                    h = (h ^ sh[k]) * 1099511628211UL;
            }
            WaterSurfaceData<SurfaceWater> water = m_WaterSystem.GetSurfaceData(out JobHandle wdeps);
            wdeps.Complete();
            Array.Clear(s.Wet, 0, w * d);
            if (water.depths.IsCreated)
                FillWet(ref water, s, x0, z0, w, d, 1f / hd.scale.x, hd.offset, ref h);
            m_Snap = s;
            m_SnapPrint = h;
            m_SnapMs = QuickRunsRules.MsSince(t0);
            return true;
        }

        private int[] m_WetA0 = new int[0], m_WetA1 = new int[0], m_WetB0 = new int[0], m_WetB1 = new int[0];
        private bool[] m_WetNode = new bool[0];

        /// <summary>The wet layer: WaterUtils.SampleDepth over WetDepthM at each texel center, as before, but it is sampled only where one of
        /// the four water nodes the bilinear sample reads is deeper than WetDepthM less a margin (a bilinear sample never exceeds its
        /// largest node, so every skipped texel is dry). Node indices are clamped exactly as SampleDepth clamps them (WaterUtils.cs:80-96).
        /// P2: at the 9 km2 cap a full sample per texel would cost a long frame. Dry land now costs a few table reads per texel.</summary>
        private void FillWet(ref WaterSurfaceData<SurfaceWater> water, HeightSnapshot s, int x0, int z0, int w, int d, float texel, float3 offset,
            ref ulong h)
        {
            int3 res = water.resolution;
            float3 sc = water.scale, of = water.offset;
            if (m_WetA0.Length < w)
            {
                m_WetA0 = new int[w];
                m_WetA1 = new int[w];
            }
            if (m_WetB0.Length < d)
            {
                m_WetB0 = new int[d];
                m_WetB1 = new int[d];
            }
            int a0 = int.MaxValue, a1 = int.MinValue, b0 = int.MaxValue, b1 = int.MinValue;
            for (int k = 0; k < w; k++)
            {
                // texel (k, l) center in world: (index / scale) - offset (the snapshot's Origin + index * texel), ToSurfaceSpace
                float x = (x0 + k) * texel - offset.x;
                int f = (int)math.floor((x + of.x) * sc.x);
                m_WetA0[k] = math.clamp(f, 0, res.x - 1);
                m_WetA1[k] = math.clamp(f + 1, 0, res.x - 1);
                a0 = Math.Min(a0, m_WetA0[k]);
                a1 = Math.Max(a1, m_WetA1[k]);
            }
            for (int l = 0; l < d; l++)
            {
                float z = (z0 + l) * texel - offset.z;
                int f = (int)math.floor((z + of.z) * sc.z);
                m_WetB0[l] = math.clamp(f, 0, res.z - 1);
                m_WetB1[l] = math.clamp(f + 1, 0, res.z - 1);
                b0 = Math.Min(b0, m_WetB0[l]);
                b1 = Math.Max(b1, m_WetB1[l]);
            }
            int nw = a1 - a0 + 1, nd = b1 - b0 + 1;
            if (m_WetNode.Length < nw * nd)
                m_WetNode = new bool[nw * nd];
            NativeArray<SurfaceWater> depths = water.depths;
            for (int j = 0; j < nd; j++)
                for (int i = 0; i < nw; i++)
                    m_WetNode[j * nw + i] = WaterUtils.ToWorldSpace(ref water, depths[(b0 + j) * res.x + a0 + i].m_Depth) > WetDepthM - 0.01f;
            for (int l = 0; l < d; l++)
            {
                int r0 = (m_WetB0[l] - b0) * nw - a0, r1 = (m_WetB1[l] - b0) * nw - a0;
                float z = (z0 + l) * texel - offset.z;
                for (int k = 0; k < w; k++)
                {
                    int i0 = m_WetA0[k], i1 = m_WetA1[k];
                    if (!m_WetNode[r0 + i0] && !m_WetNode[r0 + i1] && !m_WetNode[r1 + i0] && !m_WetNode[r1 + i1])
                        continue;
                    float x = (x0 + k) * texel - offset.x;
                    if (WaterUtils.SampleDepth(ref water, new float3(x, 0f, z)) > WetDepthM)
                    {
                        s.Wet[l * w + k] = 1;
                        h = (h ^ 0xABu) * 1099511628211UL;
                    }
                }
            }
        }

        private static HeightSnapshot Reuse(HeightSnapshot s, int x0, int z0, int w, int d, int resX, int resZ, float3 scale, float3 offset)
        {
            s.X0 = x0;
            s.Z0 = z0;
            s.Width = w;
            s.Depth = d;
            s.ResX = resX;
            s.ResZ = resZ;
            s.Scale = scale;
            s.Offset = offset;
            return s;
        }

        /// <summary>The obstacle model (BUILD-BRIEF 1): our runs (cat tracks soft), every other Surface area, every map tile the city does
        /// not own (MapTile + Native: the game's ExceedsCityLimits, Game.Areas/ValidationHelpers.cs:555-582). Bounds-filtered to the
        /// snapshot rectangle. Fingerprinted so Enter can tell a changed world.</summary>
        private void CollectObstacles(float2[] region)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            m_Obstacles = new List<AutoObstacle>();
            Bounds2 area = SnapBounds();
            ulong h = 14695981039346656037UL;
            using (var areas = m_AreaQuery.ToEntityArray(Allocator.Temp))
            using (var prefabs = m_AreaQuery.ToComponentDataArray<PrefabRef>(Allocator.Temp))
            {
                for (int i = 0; i < areas.Length; i++)
                {
                    Entity prefab = prefabs[i].m_Prefab;
                    AutoObstacleKind kind;
                    int runKind = m_Ownership.KindOf(prefab);
                    if (runKind >= 0)
                        kind = runKind == (int)RunKind.CatTrack ? AutoObstacleKind.CatTrack : AutoObstacleKind.ExistingRun;
                    else if (EntityManager.TryGetComponent(prefab, out AreaGeometryData geo))
                    {
                        if (geo.m_Type == AreaType.Surface)
                            kind = AutoObstacleKind.Surface;
                        else if (geo.m_Type == AreaType.MapTile && EntityManager.HasComponent<Native>(areas[i]))
                            kind = AutoObstacleKind.Unowned;
                        else
                            continue;
                    }
                    else
                        continue;
                    float2[] ring = RunAreaEmit.ReadRing(EntityManager, areas[i], out Bounds2 b);
                    if (ring == null || !MathUtils.Intersect(b, area))
                        continue;
                    m_Obstacles.Add(new AutoObstacle { Kind = kind, Ring = ring });
                    h = (h ^ (ulong)kind) * 1099511628211UL;
                    for (int k = 0; k < ring.Length; k++)
                    {
                        h = (h ^ (ulong)(uint)math.asint(ring[k].x)) * 1099511628211UL;
                        h = (h ^ (ulong)(uint)math.asint(ring[k].y)) * 1099511628211UL;
                    }
                }
            }
            m_ObstaclePrint = h;
            m_ObstacleMs = QuickRunsRules.MsSince(t0);
        }

        private Bounds2 SnapBounds()
        {
            float texel = 1f / m_Snap.Scale.x;
            float2 lo = new float2(m_Snap.X0 * texel - m_Snap.Offset.x, m_Snap.Z0 * texel - m_Snap.Offset.z);
            float2 hi = lo + new float2(m_Snap.Width * texel, m_Snap.Depth * texel);
            return new Bounds2(lo, hi);
        }

        /// <summary>Enter's freshness check: the same heights, water and obstacles as the scan saw. False = re-scan.</summary>
        private bool WorldUnchanged(out string what)
        {
            what = null;
            ulong snap = m_SnapPrint, obstacles = m_ObstaclePrint;
            float2[] region = m_Input.Region;
            if (region == null || !TakeSnapshot(region, out string failure))
            {
                what = "the terrain could not be read";
                return false;
            }
            CollectObstacles(region);
            if (m_SnapPrint != snap)
                what = "the terrain or water changed";
            else if (m_ObstaclePrint != obstacles)
                what = "areas or runs changed";
            return what == null;
        }


        // ---------------------------------------------------------------- preview state

        /// <summary>Enabled flags by result index (the player's left-out ids and the game's refusals stay off across re-selects), then
        /// totals and warnings. Called after every scan, toggle and build step.</summary>
        private void RefreshEnabled()
        {
            AutoPlanResult r = m_Published;
            int n = m_Phase == ToolPhase.Preview && r.Status == AutoPlanStatus.Ok ? r.Runs.Count : 0;
            if (m_EnabledByIndex.Length != n)
                m_EnabledByIndex = new bool[n];
            for (int i = 0; i < n; i++)
                m_EnabledByIndex[i] = !m_OffIds.Contains(r.Runs[i].Id) && !m_Refused.Contains(r.Runs[i].Id) && !PlanLeftOut(r, i);
            QuickRunsRules.Sum(n > 0 ? r : null, m_EnabledByIndex, m_Totals);
            RefreshWarnings();
            m_DrawDirty = true;
            Bump();
        }

        private void TogglePick(float2 hit)
        {
            AutoPlanResult r = m_Published;
            int i = QuickRunsRules.Pick(hit, r.Runs);
            if (i < 0)
            {
                Say($"QuickRuns [toggle]: no run at ({hit.x:F0}, {hit.y:F0})");
                return;
            }
            AutoRun run = r.Runs[i];
            bool nowOn;
            if (m_Refused.Remove(run.Id))
                nowOn = true;   // a refused run can be tried again
            else if (m_OffIds.Remove(run.Id))
                nowOn = true;
            else
            {
                m_OffIds.Add(run.Id);
                nowOn = false;
            }
            RefreshEnabled();
            Say(string.Format(CultureInfo.InvariantCulture,
                "QuickRuns [toggle]: run #{0} ({1} {2}, {3:F1} ha, drop {4:F0} m) {5} — {6} of {7} enabled, fee {8}",
                run.Id, run.Band, run.Kind, run.AreaM2 / 10000f, run.DropM, nowOn ? "ON" : "OFF", m_Totals.Enabled, m_Totals.Runs, m_Totals.Fee));
        }

        /// <summary>The tool's warnings next to the planner's (QuickToolWarning). Renumber: a default-named run of a kind the batch adds,
        /// at the sign it binds to, lies lower than a new run of that kind (SkiRunCatalogSystem ordinals run by top height, descending).</summary>
        private void RefreshWarnings()
        {
            AutoPlanResult r = m_Published;
            m_ToolWarn = QuickToolWarning.None;
            bool shown = m_Phase == ToolPhase.Preview && r.Status == AutoPlanStatus.Ok;
            if (shown)
            {
                if (!QuickRunsRules.CanPay(CityMoney(), m_Totals.Fee + LiftCostOn()))
                    m_ToolWarn |= QuickToolWarning.MoneyShort;
                // Anarchy sets DisableInGame at Modification5 and clears it at ModificationEnd every frame, so GameChecks in ToolUpdate never sees it.
                if (QuickRunsRules.ChecksOff(GameChecks()) || SkiOtherMods.AnarchyDetected)
                    m_ToolWarn |= QuickToolWarning.ChecksOff;
                if (m_GateOf.Length != r.Runs.Count)
                    m_GateOf = new Entity[r.Runs.Count];
                Entity first = Entity.Null;
                bool any = false;
                var topByKind = new float[5];
                for (int k = 0; k < 5; k++)
                    topByKind[k] = float.NegativeInfinity;
                for (int i = 0; i < r.Runs.Count; i++)
                {
                    m_GateOf[i] = Entity.Null;
                    if (!IsEnabled(i))
                        continue;
                    AutoRun run = r.Runs[i];
                    float2 c = QuickRunsRules.Centroid(run.Ring);
                    if (!m_Ownership.TryNearestGateEntity(c, out Entity gate, out float _))
                    {
                        m_ToolWarn |= QuickToolWarning.NoSign;
                        continue;
                    }
                    m_GateOf[i] = gate;
                    if (!any)
                    {
                        first = gate;
                        any = true;
                    }
                    else if (gate != first)
                        m_ToolWarn |= QuickToolWarning.OtherResort;
                    int kind = (int)run.Kind;
                    if (kind >= 0 && kind < 5)
                        topByKind[kind] = math.max(topByKind[kind], run.TopY);
                }
                if (any && RenumbersDefaultNames(first, topByKind))
                    m_ToolWarn |= QuickToolWarning.Renumber;
            }
            QuickRunsRules.WarningKeys(shown ? r.Warnings : AutoPlanWarning.None, m_ToolWarn, m_WarnKeys);
        }

        private bool RenumbersDefaultNames(Entity gate, float[] topByKind)
        {
            using (var areas = m_AreaQuery.ToEntityArray(Allocator.Temp))
            using (var prefabs = m_AreaQuery.ToComponentDataArray<PrefabRef>(Allocator.Temp))
            {
                for (int i = 0; i < areas.Length; i++)
                {
                    int kind = m_Ownership.KindOf(prefabs[i].m_Prefab);
                    if (kind < 0 || kind >= 5 || float.IsNegativeInfinity(topByKind[kind]))
                        continue;
                    if (!m_Ownership.TryGetBoundGate(areas[i], out Entity g) || g != gate)
                        continue;
                    if (m_NameSystem.TryGetCustomName(areas[i], out string custom) && !string.IsNullOrEmpty(custom))
                        continue;
                    DynamicBuffer<Node> nodes = EntityManager.GetBuffer<Node>(areas[i], true);
                    float top = float.NegativeInfinity;
                    for (int k = 0; k < nodes.Length; k++)
                        top = math.max(top, nodes[k].m_Position.y);
                    if (top < topByKind[kind])
                        return true;
                }
            }
            return false;
        }

        private EntityQuery m_ToolErrorQuery;
        private bool m_ToolErrorQueryMade;

        /// <summary>The game-checks state, the one source for the ChecksOff warning and the build / probe lines. The vanilla error prefabs
        /// for OverlapExisting are read as ValidationSystem reads them (its query NotificationIconData + ToolErrorData,
        /// ValidationSystem.cs:1920. FillErrorPrefabsJob skips a prefab whose flags carry DisableInGame, :1318-1325), plus
        /// ToolSystem.ignoreErrors (ToolSystem.cs:159, ToolBaseSystem.cs:535). Our own reads of vanilla prefab data: no reflection, no
        /// other mod's types (a mod that disables the check, as Anarchy does, shows up as OverlapDisabled).</summary>
        private QuickGameChecks GameChecks()
        {
            if (!m_ToolErrorQueryMade)
            {
                m_ToolErrorQuery = GetEntityQuery(ComponentType.ReadOnly<NotificationIconData>(), ComponentType.ReadOnly<ToolErrorData>());
                m_ToolErrorQueryMade = true;
            }
            int found = 0, enabled = 0;
            using (var errors = m_ToolErrorQuery.ToComponentDataArray<ToolErrorData>(Allocator.Temp))
            {
                for (int i = 0; i < errors.Length; i++)
                {
                    if (errors[i].m_Error != Game.Tools.ErrorType.OverlapExisting)
                        continue;
                    found++;
                    if ((errors[i].m_Flags & ToolErrorFlags.DisableInGame) == 0)
                        enabled++;
                }
            }
            return QuickRunsRules.ChecksState(m_ToolSystem != null && m_ToolSystem.ignoreErrors, found, enabled);
        }

        /// <summary>The cluster within 40 m of the cursor: pod labels (DECISIONS 409) and the summit line.</summary>
        private void UpdateHoverCluster()
        {
            string key = null;
            int runs = 0;
            if (m_Phase == ToolPhase.Preview && m_HasHit && !Dragging)
            {
                AutoPlanResult r = m_Published;
                float best = ClusterPickM;
                for (int c = 0; c < r.Clusters.Count; c++)
                {
                    AutoCluster cl = r.Clusters[c];
                    float d = math.distance(cl.Center, m_Hit.xz);
                    if (d > best)
                        continue;
                    best = d;
                    runs = cl.Runs;
                    key = ClusterKey(cl);
                }
            }
            if (!string.Equals(key, HoverClusterKey, StringComparison.Ordinal) || runs != HoverClusterRuns)
            {
                HoverClusterKey = key;
                HoverClusterRuns = runs;
            }
        }

        private const float ClusterPickM = 40f;

        internal static string ClusterKey(AutoCluster cl)
        {
            if (cl.Pod)
                return NoticeKey(cl.PodBand == SlopeBand.Green ? "POD[Beginner]" : "POD[MidStation]");
            return NoticeKey("CLUSTER");
        }
    }
}
