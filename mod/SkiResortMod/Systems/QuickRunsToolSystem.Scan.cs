// Quick runs tool: the scan. Copies the region's texel rectangle of the CPU heights (plus a wet layer from the water surface) into the
// planner's HeightSnapshot once, collects the obstacles (every Surface area, our runs, unowned map tiles), and steps the planner a
// fixed budget per frame (a progress line in the tooltip). On Enter the snapshot and the obstacles are read again: a changed world
// re-scans instead of building a stale plan. Warnings: the planner's, then the tool's (money, renumbering, other resort, no sign,
// game checks off).
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
        /// <summary>Planner work per frame (ms). The planner's longest unit is one SkiRunRater.Rate (~1-1.5 ms CoreCLR).</summary>
        private const float ScanBudgetMs = 4f;
        private const float WetDepthM = 0.1f;   // the cat track tool's threshold

        private readonly SkiRunAutoPlanner m_Planner = new SkiRunAutoPlanner();
        private readonly AutoPlanInput m_Input = new AutoPlanInput();
        private readonly List<AutoObstacle> m_Obstacles = new List<AutoObstacle>();
        private HeightSnapshot m_Snap;
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
            float2[] region = CornerArray();
            UpdateReadout();
            if (region == null || region.Length < 3)
            {
                Say("QuickRuns [scan]: nothing to scan — click at least three corners", NoticeKey("NEED_CORNERS"));
                return;
            }
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
            m_Knobs.Seed = SkiRunAutoPlanner.SeedOf(region, m_Shuffle);
            m_Input.Snapshot = m_Snap;
            m_Input.Region = region;
            m_Input.Obstacles = m_Obstacles;
            m_Input.Knobs = m_Knobs;
            m_Refused.Clear();
            m_ScanWhy = why;
            m_ScanT0 = System.Diagnostics.Stopwatch.GetTimestamp();
            m_ScanFrames = 0;
            AutoPlanStatus s = m_Planner.Begin(m_Input);
            Say(string.Format(CultureInfo.InvariantCulture,
                "QuickRuns [scan]: START ({0}) — {1}; snapshot {2} x {3} texels ({4:F1} ms), {5} obstacle(s) ({6:F1} ms), {7}",
                why, ReadoutText(), m_Snap.Width, m_Snap.Depth, m_SnapMs, m_Obstacles.Count, m_ObstacleMs, KnobsText(m_Knobs)));
            m_Phase = ToolPhase.Scanning;
            Bump();
            if (s != AutoPlanStatus.Running)
                FinishScan();
        }

        private void StepScan()
        {
            m_ScanFrames++;
            if (m_Planner.Step(ScanBudgetMs))
                FinishScan();
            else if ((m_ScanFrames & 7) == 0)
                Bump();   // the hub's progress bar
        }

        private void FinishScan()
        {
            AutoPlanResult r = m_Planner.Result;
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - m_ScanT0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            m_StatusKey = QuickRunsRules.StatusKey(r.Status);
            m_Phase = r.Status == AutoPlanStatus.Ok ? ToolPhase.Preview : ToolPhase.Region;
            RefreshEnabled();
            LastScanText = string.Format(CultureInfo.InvariantCulture,
                "{0}: {1} run(s) in {2} cluster(s), bands G/B/K/DB {3}/{4}/{5}/{6} (target {7}/{8}/{9}/{10}), {11} candidate(s), {12} rated; " +
                "open {13:P0}, usable {14:P0}, cliff {15:P0}, vertical {16:F0} m; {17:F0} ms over {18} frame(s), longest unit {19:F2} ms",
                r.Status, r.Runs.Count, r.Clusters.Count, r.BandCount[1], r.BandCount[2], r.BandCount[3], r.BandCount[4], r.BandTarget[1],
                r.BandTarget[2], r.BandTarget[3], r.BandTarget[4], r.Candidates, r.Rated, r.OpenShare, r.UsableShare, r.CliffShare,
                r.VerticalM, ms, m_ScanFrames, r.MaxUnitMs);
            Say($"QuickRuns [scan]: DONE ({m_ScanWhy}) — {LastScanText}; warnings {r.Warnings}; tool warnings {m_ToolWarn}",
                m_StatusKey);
            if (SkiResortSettings.DiagnosticsOn)
                ProbeScan(r, ms);
            Bump();
        }

        /// <summary>Leave the preview: Region phase; the planner keeps its result and cache (Reselect may restart from them), every
        /// reader of the result checks the phase first.</summary>
        private void DropPreview(string why)
        {
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


        /// <summary>Copies the texel rectangle of the region (SkiRunAutoPlanner.SnapshotRect) from the CPU heights, and the wet layer
        /// (WaterUtils.SampleDepth over WetDepthM at each texel center). Main thread; the vanilla precedent for reading hd.heights
        /// outside a job is SelectionToolSystem.cs:1151 (the glade tool's note).</summary>
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
            if (!SkiRunAutoPlanner.SnapshotRect(region, resX, resZ, hd.scale, hd.offset, out int x0, out int z0, out int w, out int d))
            {
                failure = "the region is degenerate";
                return false;
            }
            if ((long)w * d > SkiRunAutoPlanner.MaxLatticeCells * 4L)
            {
                failure = $"the scan rectangle is {w} x {d} texels";
                return false;
            }
            HeightSnapshot s = m_Snap != null && m_Snap.Heights.Length >= w * d && m_Snap.Wet != null && m_Snap.Wet.Length >= w * d
                ? Reuse(m_Snap, x0, z0, w, d, resX, resZ, hd.scale, hd.offset)
                : HeightSnapshot.Allocate(x0, z0, w, d, resX, resZ, hd.scale, hd.offset, wet: true);
            NativeArray<ushort> heights = hd.heights;
            ulong h = 14695981039346656037UL;
            for (int l = 0; l < d; l++)
            {
                int src = (z0 + l) * resX + x0, dst = l * w;
                for (int k = 0; k < w; k++)
                {
                    ushort v = heights[src + k];
                    s.Heights[dst + k] = v;
                    h = (h ^ v) * 1099511628211UL;
                }
            }
            WaterSurfaceData<SurfaceWater> water = m_WaterSystem.GetSurfaceData(out JobHandle wdeps);
            wdeps.Complete();
            Array.Clear(s.Wet, 0, w * d);
            if (water.depths.IsCreated)
            {
                float texel = 1f / hd.scale.x;
                for (int l = 0; l < d; l++)
                {
                    for (int k = 0; k < w; k++)
                    {
                        // texel (k, l) center in world: (index / scale) - offset (the snapshot's Origin + index * texel)
                        float x = (x0 + k) * texel - hd.offset.x, z = (z0 + l) * texel - hd.offset.z;
                        if (WaterUtils.SampleDepth(ref water, new float3(x, 0f, z)) > WetDepthM)
                        {
                            s.Wet[l * w + k] = 1;
                            h = (h ^ 0xABu) * 1099511628211UL;
                        }
                    }
                }
            }
            m_Snap = s;
            m_SnapPrint = h;
            m_SnapMs = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            return true;
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
            m_Obstacles.Clear();
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
                    float2[] ring = RingOf(areas[i], out Bounds2 b);
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
            m_ObstacleMs = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        }

        private Bounds2 SnapBounds()
        {
            float texel = 1f / m_Snap.Scale.x;
            float2 lo = new float2(m_Snap.X0 * texel - m_Snap.Offset.x, m_Snap.Z0 * texel - m_Snap.Offset.z);
            float2 hi = lo + new float2(m_Snap.Width * texel, m_Snap.Depth * texel);
            return new Bounds2(lo, hi);
        }

        private float2[] RingOf(Entity e, out Bounds2 b)
        {
            b = default;
            DynamicBuffer<Node> nodes = EntityManager.GetBuffer<Node>(e, true);
            if (nodes.Length < 3)
                return null;
            var ring = new float2[nodes.Length];
            float2 mn = new float2(float.MaxValue), mx = new float2(float.MinValue);
            for (int k = 0; k < nodes.Length; k++)
            {
                ring[k] = nodes[k].m_Position.xz;
                mn = math.min(mn, ring[k]);
                mx = math.max(mx, ring[k]);
            }
            b = new Bounds2(mn, mx);
            return ring;
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
            AutoPlanResult r = m_Planner.Result;
            int n = m_Phase == ToolPhase.Preview && r.Status == AutoPlanStatus.Ok ? r.Runs.Count : 0;
            if (m_EnabledByIndex.Length != n)
                m_EnabledByIndex = new bool[n];
            for (int i = 0; i < n; i++)
                m_EnabledByIndex[i] = !m_OffIds.Contains(r.Runs[i].Id) && !m_Refused.Contains(r.Runs[i].Id);
            QuickRunsRules.Sum(n > 0 ? r : null, m_EnabledByIndex, m_Totals);
            RefreshWarnings();
            m_DrawDirty = true;
            Bump();
        }

        private void TogglePick(float2 hit)
        {
            AutoPlanResult r = m_Planner.Result;
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
            AutoPlanResult r = m_Planner.Result;
            m_ToolWarn = QuickToolWarning.None;
            bool shown = m_Phase == ToolPhase.Preview && r.Status == AutoPlanStatus.Ok;
            if (shown)
            {
                if (!QuickRunsRules.CanPay(CityMoney(), m_Totals.Fee))
                    m_ToolWarn |= QuickToolWarning.MoneyShort;
                if (m_ToolSystem != null && m_ToolSystem.ignoreErrors)
                    m_ToolWarn |= QuickToolWarning.ChecksOff;   // ToolSystem.ignoreErrors (ToolSystem.cs:159, ToolBaseSystem.cs:535)
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
                    float2 c = Center(run.Ring);
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

        private static float2 Center(float2[] ring)
        {
            float2 s = 0f;
            if (ring == null || ring.Length == 0)
                return s;
            for (int i = 0; i < ring.Length; i++)
                s += ring[i];
            return s / ring.Length;
        }

        /// <summary>The cluster within 40 m of the cursor: pod labels (DECISIONS 409) and the summit line.</summary>
        private void UpdateHoverCluster()
        {
            string key = null;
            int runs = 0;
            if (m_Phase == ToolPhase.Preview && m_HasHit && !Dragging)
            {
                AutoPlanResult r = m_Planner.Result;
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
