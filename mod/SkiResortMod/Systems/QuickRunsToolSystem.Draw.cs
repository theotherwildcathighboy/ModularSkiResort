// Quick runs tool: the overlay. The region (white; amber near the area cap, red over it) with its corners; the candidates in the piste
// map's band colors (SkiPisteMapSystem's palette and PisteMapRules' ring pieces, so a preview looks like the runs it builds): natural
// runs dashed, left-out runs thin gray, refused runs red; a ring on each top cluster (hover: "beginner lift" / "mid-station" / "one
// lift here serves N runs"); the tree line's contour while trees are on. Segments are cached when the preview changes.
// Resort mode (A4) adds the plan from m_Published.Resort, never the live planner: a dashed chord per proposed lift (colored by its
// Home verdict, white until A6 fills it), 6 m tower discs, hatched station lots; white station rings for pinned lifts (the game draws
// the lift itself); hatched pads under the lifts' stations plus the three largest unused (benches with a dashed edge); a ring per
// pod in its class color in place of the v1 cluster rings. Everything shares the MaxPieces budget.
// Engine notes: EngineNotes/QuickRunsToolSystem.md (Overlay)

using System;
using System.Collections.Generic;
using System.Globalization;
using Colossal.Mathematics;
using Game.Rendering;
using Game.Simulation;
using Unity.Jobs;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class QuickRunsToolSystem
    {
        private const float RunWidthM = 3f;           // the piste map's BaseWidthM
        private const float OffWidthM = 1.2f;
        private const float DashM = 20f, GapM = 12f;  // the piste map's natural dash
        private const int MaxPieces = 4096;

        private static readonly UnityEngine.Color RegionColor = new UnityEngine.Color(1f, 1f, 1f, 0.85f);
        private static readonly UnityEngine.Color NearColor = new UnityEngine.Color(1f, 0.75f, 0.15f, 0.9f);
        private static readonly UnityEngine.Color OverColor = new UnityEngine.Color(0.95f, 0.2f, 0.2f, 0.95f);
        private static readonly UnityEngine.Color CornerColor = new UnityEngine.Color(0.3f, 0.6f, 1f, 0.95f);
        private static readonly UnityEngine.Color HoverColor = new UnityEngine.Color(1f, 1f, 1f, 0.95f);
        private static readonly UnityEngine.Color OffColor = new UnityEngine.Color(0.6f, 0.62f, 0.66f, 0.6f);
        private static readonly UnityEngine.Color RefusedColor = new UnityEngine.Color(0.95f, 0.15f, 0.15f, 0.95f);
        private static readonly UnityEngine.Color ClusterColor = new UnityEngine.Color(1f, 1f, 1f, 0.7f);
        private static readonly UnityEngine.Color TreeLineColor = new UnityEngine.Color(0.35f, 0.85f, 0.45f, 0.9f);
        private static readonly UnityEngine.Color LiftColor = new UnityEngine.Color(1f, 1f, 1f, 0.95f);   // Home None (until A6)
        private static readonly UnityEngine.Color DownloadColor = new UnityEngine.Color(0.62f, 0.38f, 0.95f, 0.95f);
        private static readonly UnityEngine.Color PinnedColor = new UnityEngine.Color(1f, 1f, 1f, 0.9f);
        private static readonly UnityEngine.Color PadColor = new UnityEngine.Color(1f, 1f, 1f, 0.3f);
        private static readonly UnityEngine.Color PadEdgeColor = new UnityEngine.Color(1f, 1f, 1f, 0.75f);
        private const float TowerDiscM = 6f, LiftWidthM = 2.5f, LiftDashM = 24f, LiftGapM = 14f, LiftPickM = 25f;
        private const int UnusedPadsShown = 3, BenchEdgeSides = 24;
        private const int CostFrames = 600;   // the [cost] window after each resort publish (~10 s at 60 fps)

        private struct Piece
        {
            public Line3.Segment Seg;
            public UnityEngine.Color Fill, Outline;
            public float Width, OutlineWidth;
        }

        /// <summary>One resort-plan mark: a disc at A (Size = diameter) or a dashed line A-B (Size = width), cached with the pieces.</summary>
        private struct Mark
        {
            public bool Dashed, Grid;
            public float3 A, B;
            public float Size, OutlineWidth;
            public UnityEngine.Color Fill, Outline;
        }

        private readonly List<Piece> m_Pieces = new List<Piece>();
        private readonly List<Mark> m_Marks = new List<Mark>();
        private readonly List<int> m_PadScratch = new List<int>();
        private int m_CostFrames;
        private double m_CostMs, m_CostMaxMs;
        private readonly List<float3> m_RingScratch = new List<float3>(), m_PieceA = new List<float3>(), m_PieceB = new List<float3>();
        private bool m_DrawDirty = true;

        private void Draw()
        {
            OverlayRenderSystem.Buffer buffer = m_Overlay.GetBuffer(out JobHandle deps);
            deps.Complete();
            var projected = OverlayRenderSystem.StyleFlags.Projected;
            var round = new float2(1f, 1f);
            var ring = new float2(0f, 1f);
            float k = PisteMapRules.WidthScale(SkiPisteMapSystem.ReadZoom(m_Camera));
            TerrainHeightData hd = m_TerrainSystem.GetHeightData();

            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            if (m_DrawDirty)
                BuildPieces(ref hd);
            if (m_Phase == ToolPhase.Preview || m_Phase == ToolPhase.Emitted || m_Phase == ToolPhase.Retry)
            {
                for (int i = 0; i < m_Pieces.Count; i++)
                {
                    Piece p = m_Pieces[i];
                    buffer.DrawLine(p.Outline, p.Fill, p.OutlineWidth * k, projected, p.Seg, p.Width * k, round);
                }
                AutoPlanResult r = m_Published;
                if (m_Marks.Count > 0)
                    DrawMarks(buffer, k);
                if (m_Phase == ToolPhase.Preview && (r.Resort == null || r.Resort.Pods.Length == 0))
                    for (int c = 0; c < r.Clusters.Count; c++)
                        buffer.DrawCircle(ClusterColor, ClusterColor, 0f, projected, ring, Ground(ref hd, r.Clusters[c].Center), 2f * ClusterPickM);
            }

            if (m_TreesOn && m_ContourCount > 0 && m_Snap != null)
            {
                for (int i = 0; i < m_ContourCount; i++)
                {
                    SnowLineRules.Segment s = m_Contour[i];
                    var a = new float3(s.AX, m_TreeLine, s.AZ);
                    var b = new float3(s.BX, m_TreeLine, s.BZ);
                    buffer.DrawLine(TreeLineColor, TreeLineColor, 0f, projected, new Line3.Segment(a, b), 1.5f * k, round);
                }
            }

            // The region and its corners
            IReadOnlyList<float2> pts = m_Edit.Points;
            int n = pts.Count;
            UnityEngine.Color edge = m_Readout.State == QuickRegionState.Over ? OverColor
                : m_Readout.State == QuickRegionState.Near ? NearColor : RegionColor;
            for (int i = 0; i < n; i++)
            {
                float2 xz = i == m_DragIndex ? m_DragTo : pts[i];
                bool hot = i == m_HoverPoint || i == m_DragIndex;
                UnityEngine.Color c = hot ? HoverColor : CornerColor;
                buffer.DrawCircle(c, c, 0f, projected, ring, Ground(ref hd, xz), (hot ? 10f : 7f) * math.min(k, 3f));
                if (i > 0 || n >= 3)
                {
                    int j = i > 0 ? i - 1 : n - 1;
                    float2 prev = j == m_DragIndex ? m_DragTo : pts[j];
                    buffer.DrawLine(edge, edge, 0f, projected, new Line3.Segment(Ground(ref hd, prev), Ground(ref hd, xz)), 1f * k, round);
                }
            }
            // Rubber band from the last and first corner to the cursor while the region is being drawn
            if (m_Phase == ToolPhase.Region && n >= 1 && m_HasHit && !Dragging && m_HoverPoint < 0)
            {
                buffer.DrawLine(RegionColor, RegionColor, 0f, projected, new Line3.Segment(Ground(ref hd, pts[n - 1]), m_Hit), 0.6f * k, round);
                if (n >= 2)
                    buffer.DrawLine(RegionColor, RegionColor, 0f, projected, new Line3.Segment(Ground(ref hd, pts[0]), m_Hit), 0.4f * k, round);
                buffer.DrawCircle(HoverColor, HoverColor, 0f, projected, ring, m_Hit, 3f * math.min(k, 3f));
            }
            CostSample(t0);
        }

        private void DrawMarks(OverlayRenderSystem.Buffer buffer, float k)
        {
            var projected = OverlayRenderSystem.StyleFlags.Projected;
            var grid = OverlayRenderSystem.StyleFlags.Grid | OverlayRenderSystem.StyleFlags.Projected;
            var round = new float2(1f, 1f);
            var dir = new float2(0f, 1f);
            for (int i = 0; i < m_Marks.Count; i++)
            {
                Mark m = m_Marks[i];
                if (m.Dashed)
                    buffer.DrawDashedLine(m.Outline, m.Fill, 0f, projected, new Line3.Segment(m.A, m.B), m.Size * k, LiftDashM * k, LiftGapM * k, round);
                else
                    buffer.DrawCircle(m.Outline, m.Fill, m.OutlineWidth * math.min(k, 3f), m.Grid ? grid : projected, dir, m.A, m.Size);
            }
        }

        /// <summary>The [cost] line (diagnostics only): main-thread ms of Draw over the first CostFrames frames after a resort publish.</summary>
        private void CostSample(long t0)
        {
            if (m_CostFrames <= 0 || !SkiResortMod.Settings.SkiResortSettings.DiagnosticsOn)
                return;
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            m_CostMs += ms;
            m_CostMaxMs = Math.Max(m_CostMaxMs, ms);
            if (--m_CostFrames > 0)
                return;
            Mod.Log.Info(string.Format(CultureInfo.InvariantCulture,
                "QuickRuns [cost] plan overlay: Draw {0:F3} ms mean, {1:F3} ms max over {2} frames; {3} run piece(s) + {4} plan mark(s) (cap {5})",
                m_CostMs / CostFrames, m_CostMaxMs, CostFrames, m_Pieces.Count, m_Marks.Count, MaxPieces));
        }

        /// <summary>The candidates' outlines as ring pieces (PisteMapRules.SolidRing / DashRing), colored by state.</summary>
        private void BuildPieces(ref TerrainHeightData hd)
        {
            m_DrawDirty = false;
            m_Pieces.Clear();
            m_Marks.Clear();
            if (m_Phase != ToolPhase.Preview && m_Phase != ToolPhase.Emitted && m_Phase != ToolPhase.Retry)
                return;
            AutoPlanResult r = m_Published;
            if (r.Status != AutoPlanStatus.Ok)
                return;
            if (r.Resort != null)
            {
                BuildMarks(r.Resort, ref hd);
                m_CostFrames = CostFrames;
                m_CostMs = m_CostMaxMs = 0.0;
            }
            for (int i = 0; i < r.Runs.Count && m_Pieces.Count + m_Marks.Count < MaxPieces; i++)
            {
                AutoRun run = r.Runs[i];
                if (run?.Ring == null || run.Ring.Length < 3)
                    continue;
                m_RingScratch.Clear();
                for (int v = 0; v < run.Ring.Length; v++)
                    m_RingScratch.Add(run.Nodes != null && v < run.Nodes.Length ? run.Nodes[v] : Ground(ref hd, run.Ring[v]));
                m_PieceA.Clear();
                m_PieceB.Clear();
                int room = MaxPieces - m_Pieces.Count - m_Marks.Count;
                bool dashed = PisteMapRules.IsDashed((int)run.Kind);
                int count = dashed
                    ? PisteMapRules.DashRing(m_RingScratch, DashM, GapM, room, m_PieceA, m_PieceB)
                    : PisteMapRules.SolidRing(m_RingScratch, room, m_PieceA, m_PieceB);
                UnityEngine.Color fill, outline;
                float width = RunWidthM, outlineWidth;
                if (m_Refused.Contains(run.Id))
                {
                    fill = outline = RefusedColor;
                    outlineWidth = 0f;
                }
                else if (!IsEnabled(i))
                {
                    fill = outline = OffColor;
                    outlineWidth = 0f;
                    width = OffWidthM;
                }
                else
                {
                    int style = (int)PisteMapRules.StyleOf((int)run.Band);
                    fill = SkiPisteMapSystem.PisteFill[style];
                    outline = SkiPisteMapSystem.PisteOutline[style];
                    outlineWidth = SkiPisteMapSystem.PisteOutlineWidth[style];
                }
                for (int p = 0; p < count; p++)
                    m_Pieces.Add(new Piece
                    {
                        Seg = new Line3.Segment(m_PieceA[p], m_PieceB[p]), Fill = fill, Outline = outline, Width = width, OutlineWidth = outlineWidth,
                    });
            }
        }

        /// <summary>The resort plan's marks in draw order (pods under pads under lifts), at most half the budget so the runs keep the
        /// rest. Non-finite spots are skipped.</summary>
        private void BuildMarks(ResortPlan plan, ref TerrainHeightData hd)
        {
            int cap = MaxPieces / 2;
            foreach (PlanPod pod in plan.Pods)
            {
                if (!Finite(pod.TopCenter))
                    continue;
                UnityEngine.Color c = PisteColor(SkiRunAutoPlanner.ScoreBandOf(pod.PrimaryClass));
                AddDisc(Ground(ref hd, pod.TopCenter), 2f * ClusterPickM, new UnityEngine.Color(c.r, c.g, c.b, 0.12f), c, 3f, false, cap);
            }

            // Pads: the ones a proposed lift's station uses, then the largest unused
            m_PadScratch.Clear();
            foreach (PlanLift l in plan.Lifts)
            {
                if (l.Origin != LiftOrigin.Proposed)
                    continue;
                if (l.BottomPad >= 0 && l.BottomPad < plan.Pads.Length && !m_PadScratch.Contains(l.BottomPad))
                    m_PadScratch.Add(l.BottomPad);
                if (l.TopPad >= 0 && l.TopPad < plan.Pads.Length && !m_PadScratch.Contains(l.TopPad))
                    m_PadScratch.Add(l.TopPad);
            }
            for (int n = 0; n < UnusedPadsShown; n++)
            {
                int best = -1;
                for (int i = 0; i < plan.Pads.Length; i++)
                    if (!m_PadScratch.Contains(i) && (best < 0 || plan.Pads[i].AreaM2 > plan.Pads[best].AreaM2))
                        best = i;
                if (best < 0)
                    break;
                m_PadScratch.Add(best);
            }
            foreach (int i in m_PadScratch)
            {
                PlanPad pad = plan.Pads[i];
                if (!Finite(pad.Center) || !(pad.InscribedM > 0f))
                    continue;
                AddDisc(Ground(ref hd, pad.Center), 2f * pad.InscribedM, PadColor, PadEdgeColor, pad.IsBench ? 0f : 1.5f, true, cap);
                if (pad.IsBench)
                    for (int e = 0; e < BenchEdgeSides; e += 2)   // every other side of a 24-gon: the bench's dashed edge
                    {
                        float a0 = e * 2f * math.PI / BenchEdgeSides, a1 = (e + 1) * 2f * math.PI / BenchEdgeSides;
                        float2 p0 = pad.Center + pad.InscribedM * new float2(math.cos(a0), math.sin(a0));
                        float2 p1 = pad.Center + pad.InscribedM * new float2(math.cos(a1), math.sin(a1));
                        AddLine(Ground(ref hd, p0), Ground(ref hd, p1), 1.2f, PadEdgeColor, cap);
                    }
            }

            foreach (PlanLift l in plan.Lifts)
            {
                if (!Finite(l.Bottom) || !Finite(l.Top))
                    continue;
                float[] hds = SkiRunAutoPlanner.StationHalfDiagonalM;
                float lot = 2f * hds[Math.Min((int)l.TypeIndex, hds.Length - 1)];
                float3 b = Ground(ref hd, l.Bottom.xz), t = Ground(ref hd, l.Top.xz);
                if (l.Origin == LiftOrigin.Pinned)
                {
                    var clear = new UnityEngine.Color(1f, 1f, 1f, 0f);
                    AddDisc(b, lot, clear, PinnedColor, 2f, false, cap);
                    AddDisc(t, lot, clear, PinnedColor, 2f, false, cap);
                    continue;
                }
                UnityEngine.Color c = HomeColor(l.Home);
                AddLine(b, t, LiftWidthM, c, cap);
                if (l.TowerXz != null)
                    foreach (float2 xz in l.TowerXz)
                        if (Finite(xz))
                            AddDisc(Ground(ref hd, xz), TowerDiscM, c, c, 0f, false, cap);
                var lotFill = new UnityEngine.Color(c.r, c.g, c.b, 0.45f);
                AddDisc(b, lot, lotFill, c, 1f, true, cap);
                AddDisc(t, lot, lotFill, c, 1f, true, cap);
            }
        }

        private void AddDisc(float3 at, float diameter, UnityEngine.Color fill, UnityEngine.Color outline, float outlineWidth, bool grid, int cap)
        {
            if (m_Marks.Count < cap)
                m_Marks.Add(new Mark { A = at, B = at, Size = diameter, Fill = fill, Outline = outline, OutlineWidth = outlineWidth, Grid = grid });
        }

        private void AddLine(float3 a, float3 b, float width, UnityEngine.Color c, int cap)
        {
            if (m_Marks.Count < cap)
                m_Marks.Add(new Mark { Dashed = true, A = a, B = b, Size = width, Fill = c, Outline = c });
        }

        private static UnityEngine.Color PisteColor(byte band) => SkiPisteMapSystem.PisteFill[(int)PisteMapRules.StyleOf(band)];

        private static UnityEngine.Color HomeColor(HomeVerdict h) =>
            h == HomeVerdict.Green ? PisteColor(SkiResortScore.BandGreen)
            : h == HomeVerdict.Blue ? PisteColor(SkiResortScore.BandBlue)
            : h == HomeVerdict.Black ? PisteColor(SkiResortScore.BandBlack)
            : h == HomeVerdict.DoubleBlack ? PisteColor(SkiResortScore.BandDoubleBlack)
            : h == HomeVerdict.Download ? DownloadColor : LiftColor;

        private static bool Finite(float2 v) => math.all(math.isfinite(v));

        private static bool Finite(float3 v) => math.all(math.isfinite(v));

        /// <summary>The pod whose ring (TopCenter, ClusterPickM) holds the point, -1 none; the nearest wins (A7's pick).</summary>
        internal int PodAt(float2 xz)
        {
            ResortPlan plan = m_Published.Resort;
            int best = -1;
            float bestD = ClusterPickM;
            if (plan != null)
                for (int i = 0; i < plan.Pods.Length; i++)
                {
                    float d = math.distance(plan.Pods[i].TopCenter, xz);
                    if (d <= bestD)
                    {
                        bestD = d;
                        best = i;
                    }
                }
            return best;
        }

        /// <summary>The lift whose chord passes within LiftPickM of the point, -1 none; the nearest wins (A7's pick).</summary>
        internal int LiftAt(float2 xz)
        {
            ResortPlan plan = m_Published.Resort;
            int best = -1;
            float bestD = LiftPickM;
            if (plan != null)
                for (int i = 0; i < plan.Lifts.Length; i++)
                {
                    float2 a = plan.Lifts[i].Bottom.xz, ab = plan.Lifts[i].Top.xz - a;
                    float len2 = math.lengthsq(ab);
                    float u = len2 > 1e-6f ? math.saturate(math.dot(xz - a, ab) / len2) : 0f;
                    float d = math.distance(a + u * ab, xz);
                    if (d <= bestD)
                    {
                        bestD = d;
                        best = i;
                    }
                }
            return best;
        }

        private static float3 Ground(ref TerrainHeightData hd, float2 xz) => RunAreaEmit.Ground(ref hd, xz);
    }
}
