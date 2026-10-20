// SkiPisteMapSystem — the piste-map overlay (Round E1, decision D19; docs/research/econ-round/spec/
// ch5-hub-csharp.md §8): while the hub is open with its Piste map toggle on (and the tree tool off), the
// shown ski area's run outlines are drawn on the ground, draped on the terrain, in their difficulty colors;
// natural runs are dashed, the run the mouse is over in the hub's run list is drawn twice as wide and fully
// opaque. Created as a stage-0 shell (S0, e1-spec I-22); written 2026-10-04 (Round E1, stream S5).
//
// WHAT IT READS (ch5 §4.7, C2): SkiResortHubUISystem.PisteMapVisible (armed, not disabled, the UI answered
// uiHello with contract 3, the hub open, the toggle on), ShownSign and HoveredRun; SkiGladeToolSystem
// .IsActive (C15: the tree tool draws its own outlines on the same edges, so the piste map steps aside);
// SkiRunCatalogSystem rows (Gate, Kind, Band; the catalog runs with the hub closed since E1, ch2 §2, and its
// Revision hash includes the band, so a rating recolors within a frame) and each run's Game.Areas.Node
// buffer. Nothing else: no ECS write, no component of ours.
//
// THE CACHE (§8.5): the outlines are cut ONCE into a managed stage of straight segments
// (PisteMapRules.SolidRing; natural runs PisteMapRules.DashRing — dashes cut on the CPU along the whole
// outline, C3, because OverlayRenderSystem.Buffer.DrawDashedLine restarts its pattern on every edge) and
// copied into a persistent NativeArray. Rebuilt only when the overlay becomes visible, the shown area
// changes, the catalog Revision moves, or every BackstopS while drawn (a pure move of a run keeps the
// catalog hash). A build reads the Node buffers with EntityManager.TryGetBuffer (Colossal.Entities; it
// completes the jobs writing Node — a sync point on a BUILD only, never per frame). At most MaxSegments
// segments; a run cut short is counted and warned once per load. A hover never rebuilds the cache (C2): it
// maps HoveredRun to a slot, one job parameter.
//
// THE DRAW (§8.6; the SkiRunDrawFeedbackSystem.StripDrawJob precedent, ch5 F8 / C1 — the glade tool draws
// on the main thread, the feedback system is the job precedent): one [BurstCompile] IJob per frame over the
// segment array, scheduled on OverlayRenderSystem.GetBuffer's dependencies (OverlayRenderSystem.cs:603-634)
// and registered with AddBufferWriter (:636-639); OverlayRenderSystem.OnUpdate completes every writer, then
// uploads and clears the lists (:738-741, :781, :790). Each segment: Buffer.DrawLine(outline, fill,
// outlineWidth, StyleFlags.Projected, Line3.Segment, width, roundness (1, 1)) (:207-211; Projected drapes on
// the terrain, :99-104). Width = BaseWidthM × PisteMapRules.WidthScale(camera zoom) (× HoverWidthFactor for
// the hovered run, whose colors go fully opaque). Colors are the hub's band colors (hub.module.scss
// .band*), sRGB / 255: OverlayRenderSystem converts with Color.linear (F7). The job reads no ECS data, so
// base.Dependency is neither an input nor assigned.
//
// SAFETY (the StripDrawJob rules): m_Segments is written only in Build, after CompleteDraw(); every job
// depends on the previous overlay writers, ours included, so completing m_DrawHandle completes all of ours;
// OnGamePreload, OnGameLoadingComplete, OnStopRunning and OnDestroy complete it, and OnDestroy disposes
// m_Segments after it. IJobExtensions.EarlyJobInit<PisteDrawJob>() runs once in OnCreate (try/caught), so
// the first schedule does not pay the reflection-data cost; the first draw of the PROCESS is reported apart
// (JIT) and kept out of the draw peak, as RunFeedback does.
//
// PHASE: Rendering, anchored UpdateBefore<SkiPisteMapSystem, Game.Rendering.OverlayRenderSystem> (Mod.cs;
// load-bearing: the job must be registered before OverlayRenderSystem completes and clears the lists in the
// same frame). Rendering runs paused included. It reads the hub's state written at UIUpdate earlier in the
// same MainLoop frame (SystemOrder.cs:67-68).
// COST (§8.9): hidden (hub closed, toggle off, tree tool on, or not armed) = two bool reads and a return;
// drawn and unchanged = a few compares, one camera zoom read, GetBuffer + one Schedule + AddBufferWriter
// (expected < 0.05 ms, UNVERIFIED, logged); a change = one cache build (expected 0.05-0.5 ms, logged).
//
// LOGS (§8.7): `SkiPisteMapSystem created`; the ARMED line once per load (Game mode); the first outline
// build of a load; one WARN per load each for the segment cap, a build over SlowBuildWarnMs and a draw over
// SlowDrawWarnMs; the first draw of the process; `PisteMap [cost]` after CostLogAfterDraws draws of a load;
// a one-shot `!!!` that switches the overlay off until the next load (the hub keeps working). No line per
// frame, per hover, per toggle (the hub writes the toggle line) or per later build (counted, DUMP).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Colossal.Entities;                 // EntityManager.TryGetBuffer
using Colossal.Mathematics;              // Line3
using Colossal.Serialization.Entities;   // Purpose
using Game;
using Game.Rendering;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiPisteMapSystem : GameSystemBase
    {
        private static class Tuning
        {
            public const float BaseWidthM = 3f;              // outline width at zoom ≤ RefZoomM (× PisteMapRules.WidthScale)
            public const float HoverWidthFactor = 2f;        // the hovered run
            public const float DashM = 20f, GapM = 12f;      // natural runs (world meters along the outline)
            public const float OutlineBlackM = 0.6f;         // white edge of black runs (× width scale)
            public const float OutlineDoubleBlackM = 1.0f;   // white edge of double-black runs
            public const float BackstopS = 5f;               // rebuild the cache at least this often while drawn (a pure move keeps the catalog hash)
            public const int MaxSegments = 4096;
            public const int CostLogAfterDraws = 600;
            public const float SlowDrawWarnMs = 0.5f;        // main-thread part of one draw
            public const float SlowBuildWarnMs = 2f;         // one cache build
        }

        private struct PisteSegment
        {
            public float3 A, B;
            public float Width;          // m, before the zoom scale
            public float OutlineWidth;   // m, before the zoom scale; 0 = none
            public int Slot;             // run slot in this cache (hover emphasis)
            public UnityEngine.Color Fill, Outline;
        }

        /// <summary>One draw: every cached segment into the overlay buffer. Field layout follows
        /// AreaBorderRenderSystem.cs:40-41, 97 (the Buffer field carries no attribute), as StripDrawJob.
        /// Reads only our own NativeArray; no ECS data.</summary>
        [BurstCompile]
        private struct PisteDrawJob : IJob
        {
            [ReadOnly] public NativeArray<PisteSegment> Segments;
            public int Count;
            public int HoverSlot;            // −1 = none
            public float WidthScale;         // PisteMapRules.WidthScale(zoom)
            public float HoverWidthFactor;
            public OverlayRenderSystem.Buffer Overlay;

            public void Execute()
            {
                var flags = OverlayRenderSystem.StyleFlags.Projected;          // drape on terrain (:99-104)
                var round = new float2(1f, 1f);                                // rounded joints (the glade outline's choice)
                int n = Count < Segments.Length ? Count : Segments.Length;
                for (int s = 0; s < n; s++)
                {
                    PisteSegment g = Segments[s];
                    bool hover = g.Slot == HoverSlot;
                    float k = WidthScale * (hover ? HoverWidthFactor : 1f);
                    UnityEngine.Color fill = g.Fill, outline = g.Outline;
                    if (hover)
                    {
                        fill.a = 1f;
                        outline.a = 1f;
                    }
                    Overlay.DrawLine(outline, fill, g.OutlineWidth * k, flags, new Line3.Segment(g.A, g.B), g.Width * k, round);   // :207-211
                }
            }
        }

        // Colors by (int)PisteStyle (Pending, Unrateable, Flat, Green, Blue, Black, DoubleBlack): the hub's band
        // colors, sRGB / 255 (ch5 §8.3). Outline = fill and width 0 except the black runs' white edge.
        private static readonly UnityEngine.Color[] s_Fill =
        {
            Rgb(255, 255, 255, 0.45f), Rgb(220, 150, 60, 0.9f), Rgb(170, 180, 190, 0.9f), Rgb(76, 175, 80, 0.95f),
            Rgb(52, 120, 220, 0.95f), Rgb(14, 14, 18, 0.95f), Rgb(0, 0, 0, 1f),
        };
        private static readonly UnityEngine.Color[] s_Outline =
        {
            Rgb(255, 255, 255, 0.45f), Rgb(220, 150, 60, 0.9f), Rgb(170, 180, 190, 0.9f), Rgb(76, 175, 80, 0.95f),
            Rgb(52, 120, 220, 0.95f), Rgb(255, 255, 255, 0.85f), Rgb(255, 255, 255, 0.95f),
        };
        private static readonly float[] s_OutlineWidth = { 0f, 0f, 0f, 0f, 0f, Tuning.OutlineBlackM, Tuning.OutlineDoubleBlackM };

        // Systems.
        private SkiResortHubUISystem m_Hub;
        private SkiRunCatalogSystem m_Catalog;
        private SkiGladeToolSystem m_GladeTool;
        private OverlayRenderSystem m_Overlay;
        private CameraUpdateSystem m_Camera;

        // The cache.
        private NativeArray<PisteSegment> m_Segments;                       // Persistent, MaxSegments
        private readonly PisteSegment[] m_Stage = new PisteSegment[Tuning.MaxSegments];
        private readonly List<float3> m_Ring = new List<float3>(64);
        private readonly List<float3> m_PieceA = new List<float3>(256);
        private readonly List<float3> m_PieceB = new List<float3>(256);
        private readonly Dictionary<Entity, int> m_SlotOfRun = new Dictionary<Entity, int>();
        private int m_Count, m_Runs, m_Natural;
        private Entity m_BuiltShown;
        private string m_BuiltArea = "-";
        private int m_BuiltCatalogRevision = int.MinValue;
        private float m_NextBackstop;
        private bool m_WasVisible;
        private Entity m_LastHover;
        private int m_HoverSlot = -1;
        private bool m_Truncated;
        private JobHandle m_DrawHandle;

        // Per-load counters and timings (dev tab, DUMP, the cost line).
        public int Builds, Draws;
        public float LastBuildMs, MaxBuildMs, LastDrawMs, MaxDrawMs;
        private double m_DrawMsSum, m_BuildMsSum;
        private int m_CostDraws;
        private string m_LastBuildWhy = "-";
        private float m_LastWidthScale = 1f;

        // Flags.
        private bool m_Armed, m_Disabled, m_ErrorLogged, m_FirstBuildLogged, m_TruncLogged, m_SlowDrawLogged,
            m_SlowBuildLogged, m_CostLogged;
        private static bool s_FirstDrawDone;   // per PROCESS (the JIT of Schedule<PisteDrawJob> is paid once)

        private static UnityEngine.Color Rgb(int r, int g, int b, float a) => new UnityEngine.Color(r / 255f, g / 255f, b / 255f, a);

        /// <summary>Dev tab (10 Hz, field reads only).</summary>
        public string StateText()
        {
            bool on = m_Hub != null && m_Hub.PisteMapOn;
            bool visible = m_Armed && !m_Disabled && m_Hub != null && m_Hub.PisteMapVisible && m_GladeTool != null && !m_GladeTool.IsActive;
            return $"{(on ? "on" : "off")}, visible {visible}, '{m_BuiltArea}', {m_Runs} run(s), {m_Count} segment(s), draw " +
                   $"{LastDrawMs.ToString("F3", CultureInfo.InvariantCulture)}/{MaxDrawMs.ToString("F3", CultureInfo.InvariantCulture)} ms, " +
                   $"hover slot {m_HoverSlot}";
        }

        /// <summary>The one `piste map:` DUMP line (ch5 §8.8; called by the hub's DumpToLog after the stats
        /// dump, e1-spec I-29). Appends only; never throws.</summary>
        public void AppendDump(StringBuilder sb)
        {
            try
            {
                bool hubOpen = m_Hub != null && m_Hub.IsOpen;
                bool toggle = m_Hub != null && m_Hub.PisteMapOn;
                bool contractOk = m_Hub != null && m_Hub.UiContractOk;
                bool tree = m_GladeTool != null && m_GladeTool.IsActive;
                bool visible = m_Hub != null && m_Hub.PisteMapVisible && !tree;
                Entity shown = m_Hub != null ? m_Hub.ShownSign : Entity.Null;
                string area = m_Hub != null ? m_Hub.AreaLabel(shown) : "-";
                sb.Append("  piste map: armed ").Append(m_Armed).Append(", disabled ").Append(m_Disabled)
                  .Append(", visible ").Append(visible).Append(" (hub open ").Append(hubOpen).Append(", toggle ").Append(toggle)
                  .Append(", UI contract ok ").Append(contractOk).Append(", tree tool active ").Append(tree).Append("), shown '")
                  .Append(area).Append("' ").Append(shown == Entity.Null ? "null" : shown.Index + ":" + shown.Version)
                  .Append(", runs ").Append(m_Runs).Append(" (natural ").Append(m_Natural).Append("), segments ").Append(m_Count)
                  .Append(" of ").Append(Tuning.MaxSegments).Append(m_Truncated ? ", truncated" : "")
                  .Append(", builds ").Append(Builds).Append(" (last '").Append(m_LastBuildWhy).Append("' ")
                  .Append(LastBuildMs.ToString("F2", CultureInfo.InvariantCulture)).Append(" ms, max ")
                  .Append(MaxBuildMs.ToString("F2", CultureInfo.InvariantCulture)).Append("), draws ").Append(Draws)
                  .Append(" (last ").Append(LastDrawMs.ToString("F3", CultureInfo.InvariantCulture)).Append(" ms, max ")
                  .Append(MaxDrawMs.ToString("F3", CultureInfo.InvariantCulture)).Append("), hover slot ").Append(m_HoverSlot)
                  .Append(", width scale ").Append(m_LastWidthScale.ToString("F2", CultureInfo.InvariantCulture)).Append('\n');
            }
            catch (Exception e)
            {
                sb.Append("  piste map: DUMP failed (").Append(e.GetType().Name).Append(": ").Append(e.Message).Append(")\n");
            }
        }

        // ── lifecycle ─────────────────────────────────────────────────────────────────────────────

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Hub = World.GetOrCreateSystemManaged<SkiResortHubUISystem>();
            m_Catalog = World.GetOrCreateSystemManaged<SkiRunCatalogSystem>();
            m_GladeTool = World.GetOrCreateSystemManaged<SkiGladeToolSystem>();
            m_Overlay = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
            m_Camera = World.GetOrCreateSystemManaged<CameraUpdateSystem>();
            m_Segments = new NativeArray<PisteSegment>(Tuning.MaxSegments, Allocator.Persistent);
            try
            {
                // The first job.Schedule of a process otherwise creates the job's reflection data inside the
                // first draw's timing window (SkiRunDrawFeedbackSystem.PrewarmJit, the same call).
                IJobExtensions.EarlyJobInit<PisteDrawJob>();
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"{nameof(SkiPisteMapSystem)}: EarlyJobInit<PisteDrawJob> failed ({e.GetType().Name}: {e.Message}) — " +
                             "the first draw pays the job setup instead (logged apart)");
            }
            Mod.Log.Info($"{nameof(SkiPisteMapSystem)} created");
        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            try
            {
                CompleteDraw();
            }
            catch (Exception e)
            {
                HandleException(e);
            }
            m_Armed = false;
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            try
            {
                CompleteDraw();
            }
            catch (Exception e)
            {
                Mod.Log.Error($"!!! {nameof(SkiPisteMapSystem)} EXCEPTION completing the draw job at load:\n{e}");
            }
            m_Disabled = false;
            m_ErrorLogged = false;
            m_WasVisible = false;
            m_BuiltShown = Entity.Null;
            m_BuiltArea = "-";
            m_BuiltCatalogRevision = int.MinValue;
            m_NextBackstop = 0f;
            m_LastHover = Entity.Null;
            m_HoverSlot = -1;
            m_Count = m_Runs = m_Natural = 0;
            m_SlotOfRun.Clear();
            m_Truncated = false;
            Builds = Draws = 0;
            LastBuildMs = MaxBuildMs = LastDrawMs = MaxDrawMs = 0f;
            m_DrawMsSum = m_BuildMsSum = 0.0;
            m_CostDraws = 0;
            m_LastBuildWhy = "-";
            m_LastWidthScale = 1f;
            m_FirstBuildLogged = m_TruncLogged = m_SlowDrawLogged = m_SlowBuildLogged = m_CostLogged = false;
            m_Armed = mode == GameMode.Game;
            if (!m_Armed)
                return;
            Mod.Log.Info("PisteMap: ARMED — while the hub is open with its Piste map toggle on (and the tree tool off), draws " +
                         "the shown ski area's run outlines on the ground before OverlayRenderSystem (Rendering, every frame, " +
                         "paused included): color by difficulty (flat gray, green, blue, black and double black with a white " +
                         "edge, being rated faint white, cannot be rated amber), natural runs dashed " +
                         $"DashM={Tuning.DashM:0} m / GapM={Tuning.GapM:0} m, width BaseWidthM={Tuning.BaseWidthM:0} m × zoom / " +
                         $"RefZoomM={PisteMapRules.RefZoomM:0} m (1× to MaxWidthScale={PisteMapRules.MaxWidthScale:0}×), the run " +
                         $"under the mouse in the hub's list HoverWidthFactor={Tuning.HoverWidthFactor:0}× wider; outlines cached " +
                         "(rebuilt on a ski-area change, a catalog revision, or every " +
                         $"BackstopS={Tuning.BackstopS:0} s while drawn; at most MaxSegments={Tuning.MaxSegments} segments) and " +
                         "drawn by one Burst job per frame (main thread: GetBuffer + schedule + AddBufferWriter); nothing runs " +
                         "while hidden; cost logged once per load");
        }

        protected override void OnStopRunning()
        {
            try
            {
                CompleteDraw();
            }
            catch (Exception e)
            {
                HandleException(e);
            }
            base.OnStopRunning();
        }

        protected override void OnDestroy()
        {
            try
            {
                CompleteDraw();   // never dispose m_Segments while a draw job may read it
            }
            catch (Exception e)
            {
                Mod.Log.Error($"!!! {nameof(SkiPisteMapSystem)} EXCEPTION completing the draw job in OnDestroy:\n{e}");
            }
            if (m_Segments.IsCreated)
                m_Segments.Dispose();
            base.OnDestroy();
        }

        // ── the frame (ch5 §8.4) ──────────────────────────────────────────────────────────────────

        protected override void OnUpdate()
        {
            if (m_Disabled || !m_Armed)
                return;
            try
            {
                // THE GUARD (per frame while hidden: two bool reads and a return).
                bool visible = m_Hub.PisteMapVisible && !m_GladeTool.IsActive;            // §4.7, C15
                if (!visible)
                {
                    m_WasVisible = false;
                    return;
                }

                float now = UnityEngine.Time.realtimeSinceStartup;
                Entity shown = m_Hub.ShownSign;
                int catalogRevision = m_Catalog.Revision;
                string why = !m_WasVisible ? "shown" : shown != m_BuiltShown ? "ski area changed"
                           : catalogRevision != m_BuiltCatalogRevision ? "runs changed" : now >= m_NextBackstop ? "backstop" : null;
                m_WasVisible = true;
                if (why != null)
                    Build(shown, catalogRevision, now, why);                                  // §8.5

                Entity hover = m_Hub.HoveredRun;                                              // one compare per frame
                if (hover != m_LastHover)
                {
                    m_LastHover = hover;
                    m_HoverSlot = hover != Entity.Null && m_SlotOfRun.TryGetValue(hover, out int slot) ? slot : -1;
                }
                Draw();                                                                       // §8.6
            }
            catch (Exception e)
            {
                HandleException(e);
            }
        }

        // ── the cache build (ch5 §8.5; only on a change, never per frame) ───────────────────────────

        private void Build(Entity shown, int catalogRevision, float now, string why)
        {
            CompleteDraw();   // the job must not read m_Segments while we write it
            long t0 = Stopwatch.GetTimestamp();
            m_Count = 0;
            m_Runs = 0;
            m_Natural = 0;
            m_SlotOfRun.Clear();
            int dropped = 0;
            int rows = m_Catalog.RowCount;
            for (int i = 0; i < rows; i++)
            {
                SkiRunCatalogSystem.RunRow row = m_Catalog.GetRow(i);
                if (row.Gate != shown)
                    continue;   // shown Null = the Not linked entry → the unlinked runs
                if (!EntityManager.Exists(row.Entity) ||
                    !EntityManager.TryGetBuffer(row.Entity, isReadOnly: true, out DynamicBuffer<Game.Areas.Node> nodes) ||
                    nodes.Length < 2)
                    continue;
                m_Ring.Clear();
                for (int k = 0; k < nodes.Length; k++)
                    m_Ring.Add(nodes[k].m_Position);
                m_PieceA.Clear();
                m_PieceB.Clear();
                int room = Tuning.MaxSegments - m_Count;
                bool dashed = PisteMapRules.IsDashed(row.Kind);
                int n = room <= 0 ? 0
                    : dashed ? PisteMapRules.DashRing(m_Ring, Tuning.DashM, Tuning.GapM, room, m_PieceA, m_PieceB)
                    : PisteMapRules.SolidRing(m_Ring, room, m_PieceA, m_PieceB);
                if (room <= 0 || n == room)
                    dropped++;   // the cap was reached (an exact fit also counts — harmless, the WARN is once per load)
                if (n == 0)
                    continue;
                int slot = m_Runs++;
                m_SlotOfRun[row.Entity] = slot;
                if (dashed)
                    m_Natural++;
                int style = (int)PisteMapRules.StyleOf(row.Band);
                UnityEngine.Color fill = s_Fill[style], outline = s_Outline[style];
                float outlineWidth = s_OutlineWidth[style];
                for (int k = 0; k < n && m_Count < Tuning.MaxSegments; k++)
                {
                    m_Stage[m_Count++] = new PisteSegment
                    {
                        A = m_PieceA[k], B = m_PieceB[k], Width = Tuning.BaseWidthM, OutlineWidth = outlineWidth, Slot = slot,
                        Fill = fill, Outline = outline,
                    };
                }
            }
            if (m_Count > 0)
                NativeArray<PisteSegment>.Copy(m_Stage, m_Segments, m_Count);
            m_BuiltShown = shown;
            m_BuiltCatalogRevision = catalogRevision;
            m_NextBackstop = now + Tuning.BackstopS;
            m_HoverSlot = m_LastHover != Entity.Null && m_SlotOfRun.TryGetValue(m_LastHover, out int s) ? s : -1;
            m_Truncated = dropped > 0;

            float ms = (float)((Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency);
            Builds++;
            LastBuildMs = ms;
            if (ms > MaxBuildMs)
                MaxBuildMs = ms;
            m_BuildMsSum += ms;
            m_LastBuildWhy = why;
            m_BuiltArea = m_Hub.AreaLabel(shown);   // event time (a build), log and dev-tab text
            if (!m_FirstBuildLogged)
            {
                m_FirstBuildLogged = true;
                Mod.Log.Info($"PisteMap [build] first outline build this load: '{m_BuiltArea}' — {m_Runs} run(s) ({m_Natural} " +
                             $"natural, dashed), {m_Count} segment(s) in {ms:F2} ms ({why})");
            }
            if (dropped > 0 && !m_TruncLogged)
            {
                m_TruncLogged = true;
                Mod.Log.Warn($"PisteMap [build]: '{m_BuiltArea}' needs more than MaxSegments={Tuning.MaxSegments} segment(s) — " +
                             $"{dropped} run outline(s) cut short (warned once per load)");
            }
            if (ms > Tuning.SlowBuildWarnMs && !m_SlowBuildLogged)
            {
                m_SlowBuildLogged = true;
                Mod.Log.Warn($"PisteMap [build]: one outline build took {ms:F2} ms (> {Tuning.SlowBuildWarnMs:0} ms) — " +
                             $"'{m_BuiltArea}', {m_Runs} run(s), {m_Count} segment(s) (once per load)");
            }
        }

        // ── the draw: one Burst job per frame (ch5 §8.6) ────────────────────────────────────────────

        private void Draw()
        {
            if (m_Count == 0 || !m_Segments.IsCreated)
                return;
            // Timed with raw timestamps (no allocation): GetBuffer + schedule + AddBufferWriter only.
            long t0 = Stopwatch.GetTimestamp();
            float scale = PisteMapRules.WidthScale(ReadZoom());
            m_LastWidthScale = scale;
            OverlayRenderSystem.Buffer buffer = m_Overlay.GetBuffer(out JobHandle deps);   // :603-634
            var job = new PisteDrawJob
            {
                Segments = m_Segments,
                Count = m_Count,
                HoverSlot = m_HoverSlot,
                WidthScale = scale,
                HoverWidthFactor = Tuning.HoverWidthFactor,
                Overlay = buffer,
            };
            JobHandle handle = job.Schedule(deps);   // after every earlier overlay writer, ours included
            m_Overlay.AddBufferWriter(handle);       // :636-639; completed at OverlayRenderSystem.OnUpdate :738-741
            m_DrawHandle = handle;
            float ms = (float)((Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency);
            Draws++;
            LastDrawMs = ms;
            if (!s_FirstDrawDone)
            {
                // The first draw of the PROCESS pays the one-time JIT of Schedule<PisteDrawJob>: reported on its
                // own, kept out of the peak, the mean and the slow-draw WARN.
                s_FirstDrawDone = true;
                Mod.Log.Info($"PisteMap: first overlay draw of this process {ms:F2} ms ({m_Count} segments; one-time first call " +
                             "of the draw-job schedule, kept out of the draw peak; logged once per process)");
                return;
            }
            if (ms > MaxDrawMs)
                MaxDrawMs = ms;
            m_DrawMsSum += ms;
            m_CostDraws++;
            if (ms > Tuning.SlowDrawWarnMs && !m_SlowDrawLogged)
            {
                m_SlowDrawLogged = true;
                Mod.Log.Warn($"PisteMap [cost]: one draw took {ms:F2} ms on the main thread (> {Tuning.SlowDrawWarnMs:0.0} ms) — " +
                             $"{m_Count} segment(s) (once per load)");
            }
            if (!m_CostLogged && Draws >= Tuning.CostLogAfterDraws)
            {
                m_CostLogged = true;
                double mean = m_CostDraws > 0 ? m_DrawMsSum / m_CostDraws : 0.0;
                double buildMean = Builds > 0 ? m_BuildMsSum / Builds : 0.0;
                Mod.Log.Info($"PisteMap [cost] first {Tuning.CostLogAfterDraws} draws this load: main thread mean {mean:F3} ms, max " +
                             $"{MaxDrawMs:F3} ms per draw (GetBuffer + schedule + AddBufferWriter); {Builds} outline build(s), " +
                             $"mean {buildMean:F2} ms, max {MaxBuildMs:F2} ms; last: {m_Count} segment(s) for {m_Runs} run(s)");
            }
        }

        /// <summary>The active camera controller's zoom (IGameCameraController.zoom,
        /// Game.Rendering/IGameCameraController.cs:8); 0 when there is none or it is being torn down (→ width × 1).</summary>
        private float ReadZoom()
        {
            try
            {
                IGameCameraController controller = m_Camera.activeCameraController;
                return controller != null ? controller.zoom : 0f;
            }
            catch (Exception)
            {
                return 0f;   // a controller being torn down (the CameraPoint precedent): width × 1
            }
        }

        private void CompleteDraw()
        {
            m_DrawHandle.Complete();
            m_DrawHandle = default;
        }

        private void HandleException(Exception e)
        {
            m_Disabled = true;
            try
            {
                CompleteDraw();
            }
            catch (Exception)
            {
                // already in the failure path; the "!!!" line carries the cause
            }
            if (!m_ErrorLogged)
            {
                m_ErrorLogged = true;
                Mod.Log.Error($"!!! {nameof(SkiPisteMapSystem)} EXCEPTION — the piste map is off until the next game load (the " +
                              $"hub keeps working). Please report this stack:\n{e}");
            }
        }
    }
}
