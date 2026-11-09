// Resort Planner (v2) world labels: one label per proposed lift ("P2: Detachable, 1.3 km, 540 m up, 7 runs") at the chord midpoint and one
// per pod ("Pod B: Intermediate") at its top. The labels are read from the quick runs tool's published result (never the live planner).
// They are built once per published plan (at most MaxLabels) with the same TextMeshPro mesh pattern as SkiRunLabelSystem. Pinned lifts get
// no label: the game already names an existing lift. Labels are drawn only while the tool is open in Preview. Locale:
// SkiResort.Label.PLAN_LIFT / PLAN_POD, {TYPE} = SkiResort.Hub.LIFT_TYPE[id], {CLASS} = SkiResort.Tooltip.QUICK_CLASS[band].
// Phase: Rendering, NOT anchored to OverlayRenderSystem (the labels are Graphics.DrawMesh calls, and nothing is written to the overlay
// buffers).
// Zoom: shown between the same camera zooms as SkiRunLabelSystem (RunLabelRules.FadeAlpha > 0) and fitted to the chord / ring they name
// (RunLabelRules.FitScale). The rebuild waits for a visible zoom. Pod letters come from QuickRunsPlanRules.Letter (the hub's and
// tooltip's).
// Logs: one INFO line per load, one WARN if the overlay text material is missing, a one-shot `!!!` that switches the labels off, and with
// Options > Diagnostics > Measurement diagnostics a `[cost]` line per rebuild (ms, labels built, labels dropped by the cap).
// Engine notes: EngineNotes/ResortPlanner.md (A4) and EngineNotes/Mod.md ("Near ResortPlanLabelSystem")

using System;
using System.Collections.Generic;
using System.Globalization;
using Colossal.Serialization.Entities;
using Game;
using Game.Areas;
using Game.Prefabs;
using Game.Rendering;
using Game.Simulation;
using TMPro;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace SkiResortMod.Systems
{
    public sealed partial class ResortPlanLabelSystem : GameSystemBase
    {
        private static class Tuning
        {
            public const float LiftM = 2f;            // label height above the terrain
            public const float ScaleK = 0.6f;         // x AreaUtils.CalculateLabelScale (districts are ~1.0)
            public const float FontSize = 200f;       // = SkiRunLabelSystem
            public const float SizeW = 400f, SizeH = 100f;
            public const int MaxLabels = 64;
        }

        private sealed class Part
        {
            public Mesh Mesh;
            public Material Material;
        }

        private sealed class Label
        {
            public string Text;
            public float3 Pos;
            public float TextWidth;     // local units of the mesh (before the scale)
            public float TextHeight;    // the same
            public float ExtentM;       // the chord / ring the label names
            public readonly List<Part> Parts = new List<Part>(1);
        }

        private QuickRunsToolSystem m_Tool;
        private PrefabSystem m_PrefabSystem;
        private OverlayRenderSystem m_Overlay;
        private CameraUpdateSystem m_Camera;
        private TerrainSystem m_Terrain;
        private RenderingSystem m_Rendering;
        private EntityQuery m_OverlayConfigQuery;

        private readonly List<Label> m_Labels = new List<Label>(Tuning.MaxLabels);
        private readonly Dictionary<Texture, Material> m_Materials = new Dictionary<Texture, Material>();
        private Material m_TextMaterialSource;
        private ResortPlan m_BuiltFor;               // reference compare: m_Published is an immutable snapshot per publish
        private bool m_Armed, m_Disabled, m_ErrorLogged, m_NoMaterialLogged;
        private int m_Dropped;                       // labels the cap refused in the last rebuild

        /// <summary>Counters this load (dev tab, DUMP).</summary>
        public int Rebuilds, MeshBuilds;
        public int LastDrawn { get; private set; }

        public string StateText() =>
            $"armed {m_Armed}, disabled {m_Disabled}, {m_Labels.Count} label(s), last drawn {LastDrawn}, rebuilds {Rebuilds}, mesh builds {MeshBuilds}";

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Tool = World.GetOrCreateSystemManaged<QuickRunsToolSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_Overlay = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
            m_Camera = World.GetOrCreateSystemManaged<CameraUpdateSystem>();
            m_Terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
            m_Rendering = World.GetOrCreateSystemManaged<RenderingSystem>();
            m_OverlayConfigQuery = GetEntityQuery(ComponentType.ReadOnly<OverlayConfigurationData>());
            Mod.Log.Info($"{nameof(ResortPlanLabelSystem)} created");
        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            m_Armed = false;
            ReleaseAll();
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            ReleaseAll();
            m_Disabled = m_ErrorLogged = m_NoMaterialLogged = false;
            Rebuilds = MeshBuilds = 0;
            LastDrawn = 0;
            m_Armed = mode == GameMode.Game;
            if (m_Armed)
                Mod.Log.Info($"PlanLabels: ARMED - names each proposed lift and pod of a Resort plan on the ground while the quick runs " +
                             $"tool previews it (at most {Tuning.MaxLabels}, rebuilt once per published plan)");
        }

        protected override void OnStopRunning()
        {
            ReleaseAll();
            base.OnStopRunning();
        }

        protected override void OnDestroy()
        {
            ReleaseAll();
            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            if (m_Disabled || !m_Armed)
                return;
            try
            {
                ResortPlan plan = m_Tool.IsActive && m_Tool.Phase == QuickRunsToolSystem.ToolPhase.Preview ? m_Tool.Result.Resort : null;
                if (plan == null)
                {
                    if (m_Labels.Count != 0)
                        ReleaseLabels();
                    m_BuiltFor = null;
                    return;
                }
                if (m_Rendering.hideOverlay)
                    return;
                Camera cam = m_Camera.activeCamera;
                if (cam == null)
                    return;
                if (!(RunLabelRules.FadeAlpha(ReadZoom()) > 0f))
                {
                    LastDrawn = 0;   // zoomed in or out of the labels' range: nothing to build or draw
                    return;
                }
                if (!ReferenceEquals(plan, m_BuiltFor))
                    Rebuild(plan);
                if (m_Labels.Count != 0)
                    Draw(cam);
            }
            catch (Exception e)
            {
                HandleException(e);
            }
        }

        private float ReadZoom()
        {
            try
            {
                IGameCameraController controller = m_Camera.activeCameraController;
                return controller != null ? controller.zoom : 0f;
            }
            catch (Exception)
            {
                return 0f;   // a controller being torn down: the labels are hidden for the frame
            }
        }

        private void Rebuild(ResortPlan plan)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            m_Dropped = 0;
            m_BuiltFor = plan;
            Rebuilds++;
            ReleaseLabels();
            TerrainHeightData hd = m_Terrain.GetHeightData();
            string liftT = Template("SkiResort.Label.PLAN_LIFT"), podT = Template("SkiResort.Label.PLAN_POD");
            for (int li = 0; li < plan.Lifts.Length; li++)
            {
                PlanLift l = plan.Lifts[li];
                if (l.Origin == LiftOrigin.Pinned)
                    continue;
                float2 mid = 0.5f * (l.Bottom.xz + l.Top.xz);
                string type = SkiCatalog.Lifts[Math.Min((int)l.TypeIndex, SkiCatalog.Lifts.Count - 1)].Id;
                string text = liftT.Replace("{ID}", "P" + QuickRunsPlanRules.Ordinal(plan, li).ToString(CultureInfo.InvariantCulture))
                    .Replace("{TYPE}", Template("SkiResort.Hub.LIFT_TYPE[" + type + "]"))
                    .Replace("{LENGTH}", (l.LengthM / 1000f).ToString("0.0", CultureInfo.InvariantCulture))
                    .Replace("{RISE}", l.RiseM.ToString("0", CultureInfo.InvariantCulture))
                    .Replace("{RUNS}", l.Serves.ToString(CultureInfo.InvariantCulture));
                Add(ref hd, mid, text, new Color32(255, 255, 255, 255), math.distance(l.Bottom.xz, l.Top.xz));
            }
            foreach (PlanPod pod in plan.Pods)
            {
                string text = podT.Replace("{ID}", QuickRunsPlanRules.Letter(pod.Id)).Replace("{CLASS}", Template("SkiResort.Tooltip.QUICK_CLASS[" + pod.PrimaryClass + "]"));
                RunLabelRules.BandRgb(PlanGeometry.ScoreBandOf(pod.PrimaryClass), out byte r, out byte g, out byte b);
                Add(ref hd, pod.TopCenter, text, new Color32(r, g, b, 255), 2f * QuickRunsPlanRules.PodPickM);
            }
            if (SkiResortMod.Settings.SkiResortSettings.DiagnosticsOn)
                Mod.Log.Info(string.Format(CultureInfo.InvariantCulture,
                    "PlanLabels [cost] rebuild: {0:F3} ms for {1} label(s) ({2} mesh build(s) so far), {3} dropped by the cap of {4}",
                    (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency,
                    m_Labels.Count, MeshBuilds, m_Dropped, Tuning.MaxLabels));
        }

        private void Add(ref TerrainHeightData hd, float2 xz, string text, Color32 color, float extentM)
        {
            if (!math.all(math.isfinite(xz)))
                return;
            if (m_Labels.Count >= Tuning.MaxLabels)
            {
                m_Dropped++;
                return;
            }
            var pos = new float3(xz.x, 0f, xz.y);
            pos.y = TerrainUtils.SampleHeight(ref hd, pos) + Tuning.LiftM;
            var label = new Label { Text = text, Pos = pos, ExtentM = extentM };
            BuildMeshes(label, color);
            if (label.Parts.Count != 0)
                m_Labels.Add(label);
        }

        /// <summary>The game's TextMeshPro text mesh -> one Mesh per atlas page (SkiRunLabelSystem.BuildMeshes).</summary>
        private void BuildMeshes(Label label, Color32 color)
        {
            TextMeshPro tmp = m_Overlay.GetTextMesh();
            Color keepColour = tmp.color;
            bool keepRich = tmp.richText;
            try
            {
                tmp.richText = false;
                tmp.color = color;
                tmp.rectTransform.sizeDelta = new Vector2(Tuning.SizeW, Tuning.SizeH);
                tmp.fontSize = Tuning.FontSize;
                tmp.alignment = TextAlignmentOptions.Center;
                TMP_TextInfo info = tmp.GetTextInfo(label.Text);
                float minX = float.PositiveInfinity, maxX = float.NegativeInfinity, minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
                for (int i = 0; i < info.meshInfo.Length; i++)
                {
                    TMP_MeshInfo mi = info.meshInfo[i];
                    int vc = mi.vertexCount;
                    if (vc <= 0)
                        continue;
                    Material material = MaterialFor(mi.material);
                    if (material == null)
                        continue;
                    var vertices = new Vector3[vc];
                    var uv0 = new Vector2[vc];
                    var uv2 = new Vector2[vc];
                    var colours = new Color32[vc];
                    var triangles = new int[(vc >> 2) * 6];
                    Array.Copy(mi.vertices, 0, vertices, 0, vc);
                    Array.Copy(mi.uvs0, 0, uv0, 0, vc);
                    Array.Copy(mi.uvs2, 0, uv2, 0, vc);
                    Array.Copy(mi.colors32, 0, colours, 0, vc);
                    Array.Copy(mi.triangles, 0, triangles, 0, triangles.Length);
                    for (int k = 0; k < vc; k++)
                    {
                        if (vertices[k].x < minX) minX = vertices[k].x;
                        if (vertices[k].x > maxX) maxX = vertices[k].x;
                        if (vertices[k].y < minY) minY = vertices[k].y;
                        if (vertices[k].y > maxY) maxY = vertices[k].y;
                    }
                    var mesh = new Mesh { name = "ResortPlanLabel " + label.Text };
                    mesh.vertices = vertices;
                    mesh.uv = uv0;
                    mesh.uv2 = uv2;
                    mesh.colors32 = colours;
                    mesh.triangles = triangles;
                    mesh.RecalculateBounds();
                    label.Parts.Add(new Part { Mesh = mesh, Material = material });
                }
                label.TextWidth = maxX > minX ? maxX - minX : 0f;
                label.TextHeight = maxY > minY ? maxY - minY : 0f;
                MeshBuilds++;
            }
            finally
            {
                tmp.color = keepColour;                                   // the shared mesh also draws the water-source names
                tmp.richText = keepRich;
            }
        }

        private Material MaterialFor(Material tmpMaterial)
        {
            Texture atlas = tmpMaterial != null ? tmpMaterial.mainTexture : null;
            if (atlas == null)
                return null;
            if (m_Materials.TryGetValue(atlas, out Material cached) && cached != null)
                return cached;
            if (m_TextMaterialSource == null)
            {
                if (m_OverlayConfigQuery.IsEmptyIgnoreFilter ||
                    !m_PrefabSystem.TryGetSingletonPrefab(m_OverlayConfigQuery, out OverlayConfigurationPrefab config) ||
                    config.m_TextMaterial == null)
                {
                    if (!m_NoMaterialLogged)
                    {
                        m_NoMaterialLogged = true;
                        Mod.Log.Warn("PlanLabels: the overlay text material is not available - no plan labels until the next load");
                    }
                    return null;
                }
                m_TextMaterialSource = config.m_TextMaterial;
            }
            var material = new Material(m_TextMaterialSource) { name = "ResortPlanLabel text" };
            m_Overlay.CopyFontAtlasParameters(tmpMaterial, material);
            m_Materials[atlas] = material;
            return material;
        }

        private void Draw(Camera cam)
        {
            float3 camPos = m_Camera.position;
            quaternion q = AreaUtils.CalculateLabelRotation(cam.transform.right);
            var rotation = new Quaternion(q.value.x, q.value.y, q.value.z, q.value.w);
            float3 axisX = math.rotate(q, new float3(1f, 0f, 0f));
            float3 axisY = math.rotate(q, new float3(0f, 1f, 0f));
            TerrainHeightData hd = m_Terrain.GetHeightData();
            int drawn = 0;
            foreach (Label label in m_Labels)
            {
                float scale = AreaUtils.CalculateLabelScale(camPos, label.Pos) * Tuning.ScaleK;
                scale = RunLabelRules.FitScale(scale, label.TextWidth, label.ExtentM);
                float y = SkiRunLabelSystem.ClearY(ref hd, label.Pos, axisX, axisY, 0.5f * label.TextWidth * scale, 0.5f * label.TextHeight * scale, Tuning.LiftM);
                Matrix4x4 matrix = Matrix4x4.TRS(new Vector3(label.Pos.x, y, label.Pos.z), rotation, new Vector3(scale, scale, scale));
                foreach (Part part in label.Parts)
                    Graphics.DrawMesh(part.Mesh, matrix, part.Material, 0, cam, 0, null, false, false);
                drawn++;
            }
            LastDrawn = drawn;
        }

        private static string Template(string key)
        {
            var dictionary = Game.SceneFlow.GameManager.instance?.localizationManager?.activeDictionary;
            return dictionary != null && dictionary.TryGetValue(key, out string text) && !string.IsNullOrEmpty(text) ? text : key;
        }

        private void ReleaseLabels()
        {
            foreach (Label label in m_Labels)
                foreach (Part part in label.Parts)
                    if (part.Mesh != null)
                        UnityEngine.Object.Destroy(part.Mesh);
            m_Labels.Clear();
            LastDrawn = 0;
        }

        private void ReleaseAll()
        {
            ReleaseLabels();
            foreach (Material m in m_Materials.Values)
                if (m != null)
                    UnityEngine.Object.Destroy(m);
            m_Materials.Clear();
            m_TextMaterialSource = null;
            m_BuiltFor = null;
        }

        private void HandleException(Exception e)
        {
            m_Disabled = true;
            if (!m_ErrorLogged)
            {
                m_ErrorLogged = true;
                Mod.Log.Error($"!!! {nameof(ResortPlanLabelSystem)} EXCEPTION - the plan labels are off until the next game load " +
                              $"(everything else keeps working). Please report this stack:\n{e}");
            }
            try
            {
                ReleaseAll();
            }
            catch (Exception)
            {
                // already in the failure path. The "!!!" line carries the cause
            }
        }
    }
}
