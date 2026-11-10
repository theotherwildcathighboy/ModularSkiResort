// The plan marks outside the Resort Planner (task R1-c, plan section 3.4): the plan sheet's lifts that are not built yet, drawn while the
// player works on lifts with another tool. Shown only while the lift-line tool, the station placer, or the vanilla object tool with one
// of our lift stations is the active tool (the station test is LiftLineHotkeySystem's). The planner tool draws its own marks.
// Per row (not Built): the dashed chord between the two spots and a lot disc at each spot whose station is not placed yet, the planner's
// sizes (PlanGeometry.StationHalfDiagonalM, QuickRunsToolSystem.Draw.cs). Color by state: Proposed neutral, StationsPlaced and
// NeedsFitting amber, Refused red. A row switched off is dimmed.
// Draw: Rendering, UpdateBefore<ResortPlanMarksSystem, Game.Rendering.OverlayRenderSystem> (Mod.cs). Load-bearing: OverlayRenderSystem
// consumes and clears the overlay lists later in the same phase (OverlayRenderSystem.cs:781, :790). Main-thread write: GetBuffer, then
// complete its writers (OverlayRenderSystem.cs:603-634), as SkiStationAlignSystem does.
// Logs (silence impossible): ARMED / NOT ARMED per load, one line when the marks show and when they hide. An exception disables the marks
// until the next load.
// Engine notes: EngineNotes/StationPlaceToolSystem.md (Plan marks outside the planner)

using System;
using Colossal.Mathematics;
using Colossal.Serialization.Entities;
using Game;
using Game.Rendering;
using Game.Simulation;
using Game.Tools;
using Unity.Jobs;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class ResortPlanMarksSystem : GameSystemBase
    {
        private const float ChordWidthM = 2.5f, DashM = 24f, GapM = 14f, LotEdgeM = 2f;

        private static readonly UnityEngine.Color ProposedColor = new UnityEngine.Color(0.92f, 0.95f, 1f, 0.85f);
        private static readonly UnityEngine.Color PlacedColor = new UnityEngine.Color(1f, 0.72f, 0.2f, 0.9f);
        private static readonly UnityEngine.Color RefusedColor = new UnityEngine.Color(0.95f, 0.3f, 0.25f, 0.9f);

        private ToolSystem m_ToolSystem;
        private ObjectToolSystem m_ObjectTool;
        private LiftLineToolSystem m_LiftTool;
        private StationPlaceToolSystem m_Placer;
        private ResortLiftBuildSystem m_LiftBuild;
        private OverlayRenderSystem m_Overlay;
        private TerrainSystem m_TerrainSystem;
        private CameraUpdateSystem m_Camera;
        private bool m_Armed, m_Disabled, m_Shown;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_ObjectTool = World.GetOrCreateSystemManaged<ObjectToolSystem>();
            m_LiftTool = World.GetOrCreateSystemManaged<LiftLineToolSystem>();
            m_Placer = World.GetOrCreateSystemManaged<StationPlaceToolSystem>();
            m_LiftBuild = World.GetOrCreateSystemManaged<ResortLiftBuildSystem>();
            m_Overlay = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
            m_TerrainSystem = World.GetOrCreateSystemManaged<TerrainSystem>();
            m_Camera = World.GetOrCreateSystemManaged<CameraUpdateSystem>();
            Mod.Log.Info($"{nameof(ResortPlanMarksSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Disabled = m_Shown = false;
            m_Armed = mode == GameMode.Game;
            Mod.Log.Info(m_Armed
                ? "ResortLifts [marks]: ARMED — the plan sheet's lifts not built yet show while a lift tool or one of our stations is in hand"
                : $"ResortLifts [marks]: NOT ARMED (mode {mode})");
        }

        protected override void OnUpdate()
        {
            if (!m_Armed || m_Disabled)
                return;
            try
            {
                ResortPlanSheet sheet = m_LiftBuild.IsArmed ? m_LiftBuild.Sheet : null;
                string tool = LiftToolInHand();
                int rows = sheet != null ? sheet.Count - sheet.CountIn(SheetLiftState.Built) : 0;
                bool show = tool != null && rows > 0;
                if (show != m_Shown)
                {
                    m_Shown = show;
                    Mod.Log.Info(show
                        ? $"ResortLifts [marks]: SHOWN — {rows} planned lift(s) not built, tool {tool}"
                        : "ResortLifts [marks]: HIDDEN — " + (tool == null ? "no lift tool in hand" : "no planned lift left to build"));
                }
                if (show)
                    Draw(sheet);
            }
            catch (Exception e)
            {
                m_Disabled = true;
                Mod.Log.Error($"!!! {nameof(ResortPlanMarksSystem)} EXCEPTION — the plan marks outside the planner are off until the next game " +
                              $"load. Please report this stack:\n{e}");
            }
        }

        /// <summary>The active tool's id when it is a lift tool (the lift-line tool, the station placer, the object tool with one of our
        /// stations), else null.</summary>
        private string LiftToolInHand()
        {
            ToolBaseSystem active = m_ToolSystem.activeTool;
            if (active == null)
                return null;
            if (active == m_LiftTool || active == m_Placer)
                return active.toolID;
            if (active == m_ObjectTool && m_ObjectTool.prefab != null && SkiCatalog.StationByPrefab(m_ObjectTool.prefab.name) != null)
                return active.toolID + " (" + m_ObjectTool.prefab.name + ")";
            return null;
        }

        private void Draw(ResortPlanSheet sheet)
        {
            OverlayRenderSystem.Buffer buffer = m_Overlay.GetBuffer(out JobHandle deps);
            deps.Complete();
            TerrainHeightData hd = m_TerrainSystem.GetHeightData();
            float k = PisteMapRules.WidthScale(SkiPisteMapSystem.ReadZoom(m_Camera));
            var projected = OverlayRenderSystem.StyleFlags.Projected;
            var round = new float2(1f, 1f);
            var ring = new float2(0f, 1f);
            float[] hds = PlanGeometry.StationHalfDiagonalM;
            foreach (ResortPlanSheet.Row r in sheet.Rows)
            {
                if (r.State == SheetLiftState.Built || !Finite(r.Bottom.xz) || !Finite(r.Top.xz))
                    continue;
                UnityEngine.Color c = r.State == SheetLiftState.Refused ? RefusedColor
                    : r.State == SheetLiftState.Proposed ? ProposedColor : PlacedColor;
                if (!r.On)
                    c.a *= 0.4f;
                float3 b = RunAreaEmit.Ground(ref hd, r.Bottom.xz), t = RunAreaEmit.Ground(ref hd, r.Top.xz);
                buffer.DrawDashedLine(c, c, 0f, projected, new Line3.Segment(b, t), ChordWidthM * k, DashM * k, GapM * k, round);
                float lot = 2f * hds[Math.Min((int)r.TypeIndex, hds.Length - 1)];
                var fill = new UnityEngine.Color(c.r, c.g, c.b, c.a * 0.3f);
                if (r.BaseStation.IsNull)
                    buffer.DrawCircle(c, fill, LotEdgeM * math.min(k, 3f), projected, ring, b, lot);
                if (r.TopStation.IsNull)
                    buffer.DrawCircle(c, fill, LotEdgeM * math.min(k, 3f), projected, ring, t, lot);
            }
        }

        private static bool Finite(float2 v) => math.all(math.isfinite(v));
    }
}
