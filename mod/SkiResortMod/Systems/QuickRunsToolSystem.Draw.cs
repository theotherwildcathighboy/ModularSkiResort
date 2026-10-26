// Quick runs tool: the overlay. The region (white; amber near the area cap, red over it) with its corners; the candidates in the piste
// map's band colors (SkiPisteMapSystem's palette and PisteMapRules' ring pieces, so a preview looks like the runs it builds): natural
// runs dashed, left-out runs thin gray, refused runs red; a ring on each top cluster (hover: "beginner lift" / "mid-station" / "one
// lift here serves N runs"); the tree line's contour while trees are on. Segments are cached when the preview changes.
// Engine notes: EngineNotes/QuickRunsToolSystem.md (Overlay)

using System;
using System.Collections.Generic;
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

        private struct Piece
        {
            public Line3.Segment Seg;
            public UnityEngine.Color Fill, Outline;
            public float Width, OutlineWidth;
        }

        private readonly List<Piece> m_Pieces = new List<Piece>();
        private readonly List<float3> m_RingScratch = new List<float3>(), m_PieceA = new List<float3>(), m_PieceB = new List<float3>();
        private bool m_DrawDirty = true;

        private void Draw()
        {
            OverlayRenderSystem.Buffer buffer = m_Overlay.GetBuffer(out JobHandle deps);
            deps.Complete();
            var projected = OverlayRenderSystem.StyleFlags.Projected;
            var round = new float2(1f, 1f);
            var ring = new float2(0f, 1f);
            float k = PisteMapRules.WidthScale(ReadZoom());
            TerrainHeightData hd = m_TerrainSystem.GetHeightData();

            if (m_DrawDirty)
                BuildPieces(ref hd);
            if (m_Phase == ToolPhase.Preview || m_Phase == ToolPhase.Emitted || m_Phase == ToolPhase.Retry)
            {
                for (int i = 0; i < m_Pieces.Count; i++)
                {
                    Piece p = m_Pieces[i];
                    buffer.DrawLine(p.Outline, p.Fill, p.OutlineWidth * k, projected, p.Seg, p.Width * k, round);
                }
                AutoPlanResult r = m_Planner.Result;
                if (m_Phase == ToolPhase.Preview)
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
        }

        /// <summary>The candidates' outlines as ring pieces (PisteMapRules.SolidRing / DashRing), colored by state.</summary>
        private void BuildPieces(ref TerrainHeightData hd)
        {
            m_DrawDirty = false;
            m_Pieces.Clear();
            if (m_Phase != ToolPhase.Preview && m_Phase != ToolPhase.Emitted && m_Phase != ToolPhase.Retry)
                return;
            AutoPlanResult r = m_Planner.Result;
            if (r.Status != AutoPlanStatus.Ok)
                return;
            for (int i = 0; i < r.Runs.Count && m_Pieces.Count < MaxPieces; i++)
            {
                AutoRun run = r.Runs[i];
                if (run?.Ring == null || run.Ring.Length < 3)
                    continue;
                m_RingScratch.Clear();
                for (int v = 0; v < run.Ring.Length; v++)
                    m_RingScratch.Add(run.Nodes != null && v < run.Nodes.Length ? run.Nodes[v] : Ground(ref hd, run.Ring[v]));
                m_PieceA.Clear();
                m_PieceB.Clear();
                int room = MaxPieces - m_Pieces.Count;
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

        private static float3 Ground(ref TerrainHeightData hd, float2 xz) => RunAreaEmit.Ground(ref hd, xz);

        /// <summary>The active camera controller's zoom (SkiPisteMapSystem.ReadZoom's idiom); 0 = width x1.</summary>
        private float ReadZoom()
        {
            try
            {
                var controller = m_Camera.activeCameraController;
                return controller != null ? controller.zoom : 0f;
            }
            catch (Exception)
            {
                return 0f;
            }
        }
    }
}
