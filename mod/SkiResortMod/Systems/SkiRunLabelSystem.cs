// SkiRunLabelSystem — the run names on the hill (ticket T37, 2026-10-16; docs/research/field/2026-10-15/build/
// T37-run-labels.md). Each of our runs gets its name written flat on the ground at the run's centre, in the piste-map colour
// of its difficulty, at the zoom range where the piste map reads, faded in and out like the district names. Default on
// (option ShowRunNamesOnTheHill, Richard's ruling T37). A summer player sees where the runs are and what they are called.
//
// WHICH MECHANISM (the decompile, 1.6.0f1 baseline). Vanilla's district / street names are NOT one reusable "label"
// service:
//   * Streets (Game.Rendering/AggregateMeshSystem, NetLabel prefab) and districts (AreaBufferSystem name mesh, DistrictPrefab)
//     each build a per-entity LabelVertex buffer (a TextMeshPro text mesh, AreaBufferSystem.UpdateLabelVertices :1010-1196) and
//     draw it with a per-area-type NAME material (AreaTypePrefab.m_NameMaterial, set up :816-821; drawn AreaRenderSystem :81-100).
//   * The area path cannot be pointed at a run: runs are Surface areas; the LabelVertex / LabelExtents buffers exist only in the
//     DistrictPrefab archetype (DistrictPrefab.cs:30-31); UpdateLabelVertices walks EVERY area of the type and indexes the
//     buffer accessor (:1033-1036), which would need the buffer on every building's grass sub-area; the colour is one
//     AreaNameData per PREFAB (:481-492), not per run; and the name material belongs to the vanilla Surface AreaTypePrefab.
//   * OverlayRenderSystem.Buffer.DrawText(entity, pos, cameraFace) (:189-198) is the public text path (GuideLinesSystem uses it for
//     water-source names), but it has no colour, no scale (the matrix is TRS(pos, identity, 1), :193) and no ground orientation.
// SO this system builds its labels the way those two do and reuses everything below the per-entity buffer:
//   * the game's own TextMeshPro text mesh and font: OverlayRenderSystem.GetTextMesh() (:641), the same size / alignment settings
//     (:708-712, AreaBufferSystem :1017-1019), the same mesh copy (FillMeshFromTMPMeshInfo :674-698);
//   * the game's own overlay TEXT material (OverlayConfigurationPrefab.m_TextMaterial, as GetOverlayTextMaterial :1147-1153) with
//     the font atlas copied across (OverlayRenderSystem.CopyFontAtlasParameters :667-672);
//   * the district labels' geometry: AreaUtils.CalculateLabelRotation (flat on the ground, reads along the camera's right,
//     AreaUtils.cs:115-119) and AreaUtils.CalculateLabelScale (sqrt(distance x 0.001), :126-129) — the same size-by-distance rule;
//   * the district labels' anchor: Geometry.m_CenterPosition (AreaUtils.CalculateLabelPosition :121-124). GeometrySystem computes
//     it as the point INSIDE the area that is furthest from every edge (Game.Areas/GeometrySystem.cs:520-585, the best
//     CheckCenterPositionCandidate), so a concave run's label never lands outside it — no centroid fix-up needed. The catalog row
//     already carries it (RunRow.CenterX / CenterZ); y is the terrain height + LiftM.
// What is OURS: the per-run mesh (colour and fade in the vertex colours), the fade rule (RunLabelRules.FadeAlpha — the vanilla
// district fade is inside its shader, which the decompile does not show), and the draw (Graphics.DrawMesh, the camera of the game).
//
// GATES (cheapest first): option ShowRunNamesOnTheHill; Game mode armed; RenderingSystem.hideOverlay (photo mode / screenshot hides
// the district names the same way, AreaRenderSystem :81); the fade (zoom outside the range → nothing, nothing rebuilt); a tool
// working on runs — the area tool holding one of our run prefabs or the tree (glade) tool active → no labels at all; any other tool
// with Temp copies of runs (an edit of an existing run) → just that run's label is skipped. The game has no general "show names"
// setting for areas (searched Game.Settings and the interface settings: none); hideOverlay is the one switch it honours.
//
// THE CACHE. Rebuilt only when the catalog moved (SkiRunCatalogSystem.Revision, or Rebuilds — a pure move of a run keeps the
// Revision hash, the rows' centres are refreshed by every catalog rebuild) AND the labels are about to be drawn. A rebuild reads
// the rows (entity, name, band, centre, outline), samples the terrain once per label, and rebuilds a label's text mesh only when
// its (clean text, band) changed. The fade is vertex alpha: rewritten for every label only when the quantised step moves.
//
// PHASE: Rendering, but NOT anchored to OverlayRenderSystem (nothing is written to its buffers: this is Graphics.DrawMesh).
// COST: option off / out of zoom range / hidden = a bool read, a zoom read and a return. Drawn and unchanged = one DrawMesh per
// label within the cull radius (RunLabelRules.CullRadius) — a few dozen on a big resort (UNVERIFIED, expected well under 0.1 ms);
// a rebuild = one pass over the rows + one TMP text mesh per changed label (UNVERIFIED, expected ~0.05 ms per label).
//
// LOGS: `SkiRunLabelSystem created`; ONE INFO line per load (ARMED); one WARN if the overlay text material is missing; a one-shot
// `!!!` that switches the labels off until the next load. Nothing per frame, per rebuild or per label.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Colossal.Serialization.Entities;
using Game;
using Game.Areas;
using Game.Prefabs;
using Game.Rendering;
using Game.Simulation;
using Game.Tools;
using TMPro;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiRunLabelSystem : GameSystemBase
    {
        private static class Tuning
        {
            public const float LiftM = 1.5f;          // label height above the terrain at the run's centre
            public const float ScaleK = 0.7f;         // x AreaUtils.CalculateLabelScale (districts are ~1.0): runs are smaller than districts
            public const float FontSize = 200f;       // = OverlayRenderSystem.GetTextRenderItems / AreaBufferSystem.UpdateLabelVertices
            public const float SizeW = 250f, SizeH = 100f;
            public const int MaxLabels = 512;         // a hard cap on the cache (a resort has tens of runs)
        }

        /// <summary>One mesh of a label (one per font atlas page the text touches; normally one).</summary>
        private sealed class Part
        {
            public Mesh Mesh;
            public Material Material;
            public Color32[] Colours;   // the vertex colours at alpha 255 (rgb = the band colour)
            public Color32[] Scratch;   // the colours with the current alpha step applied
        }

        private sealed class Label
        {
            public Entity Run;
            public string Text;
            public sbyte Band;
            public float3 Pos;          // world (x, terrain + LiftM, z)
            public float TextWidth;     // local units of the mesh (before the scale)
            public float ExtentM;       // RunLabelRules.Extent of the outline
            public int AppliedStep = -1;
            public readonly List<Part> Parts = new List<Part>(1);
        }

        private ToolSystem m_ToolSystem;
        private AreaToolSystem m_AreaTool;
        private PrefabSystem m_PrefabSystem;
        private SkiRunOwnershipSystem m_Ownership;
        private SkiRunCatalogSystem m_Catalog;
        private SkiGladeToolSystem m_GladeTool;
        private OverlayRenderSystem m_Overlay;
        private CameraUpdateSystem m_Camera;
        private TerrainSystem m_Terrain;
        private RenderingSystem m_Rendering;
        private EntityQuery m_OverlayConfigQuery, m_TempQuery;

        // The cache.
        private readonly Dictionary<Entity, Label> m_Labels = new Dictionary<Entity, Label>();
        private readonly List<Label> m_Draw = new List<Label>(64);
        private readonly Dictionary<Texture, Material> m_Materials = new Dictionary<Texture, Material>();
        private readonly HashSet<Entity> m_Seen = new HashSet<Entity>();
        private readonly List<Entity> m_Gone = new List<Entity>(8);
        private readonly HashSet<Entity> m_Editing = new HashSet<Entity>();
        private Material m_TextMaterialSource;
        private int m_BuiltRevision = int.MinValue;
        private long m_BuiltRebuilds = -1;
        private int m_Step = -1;

        // tool prefab reference → "is one of our run prefabs" cache (re-resolved only when the reference changes).
        private PrefabBase m_AreaPrefabRef;
        private bool m_AreaPrefabIsRun;

        // Per-load state.
        private bool m_Armed, m_Disabled, m_ErrorLogged, m_NoMaterialLogged;

        /// <summary>Counters this load (dev tab, DUMP).</summary>
        public int Rebuilds, MeshBuilds, Draws;
        public int LabelCount => m_Labels.Count;
        public int LastDrawn { get; private set; }

        public string StateText()
        {
            return $"armed {m_Armed}, disabled {m_Disabled}, {m_Labels.Count} label(s), last drawn {LastDrawn}, step {m_Step}, " +
                   $"rebuilds {Rebuilds}, mesh builds {MeshBuilds}";
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_AreaTool = World.GetOrCreateSystemManaged<AreaToolSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_Ownership = World.GetOrCreateSystemManaged<SkiRunOwnershipSystem>();
            m_Catalog = World.GetOrCreateSystemManaged<SkiRunCatalogSystem>();
            m_GladeTool = World.GetOrCreateSystemManaged<SkiGladeToolSystem>();
            m_Overlay = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
            m_Camera = World.GetOrCreateSystemManaged<CameraUpdateSystem>();
            m_Terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
            m_Rendering = World.GetOrCreateSystemManaged<RenderingSystem>();
            m_OverlayConfigQuery = GetEntityQuery(ComponentType.ReadOnly<OverlayConfigurationData>());
            m_TempQuery = GetEntityQuery(ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Surface>());
            Mod.Log.Info($"{nameof(SkiRunLabelSystem)} created");
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
            m_Disabled = false;
            m_ErrorLogged = false;
            m_NoMaterialLogged = false;
            m_AreaPrefabRef = null;
            m_AreaPrefabIsRun = false;
            Rebuilds = MeshBuilds = Draws = 0;
            LastDrawn = 0;
            m_Armed = mode == GameMode.Game;
            if (!m_Armed)
                return;
            SkiResortMod.Settings.SkiResortSettings s = Mod.Settings;
            Mod.Log.Info("RunLabels: ARMED — writes each run's name flat on the ground at its centre (Geometry.m_CenterPosition, the " +
                         "district-label anchor), in the piste-map colour of its band, with the game's own TextMeshPro text mesh and overlay " +
                         $"text material (option ShowRunNamesOnTheHill, now {(s == null ? "default on" : s.ShowRunNamesOnTheHill ? "on" : "off")}); " +
                         $"visible between camera zoom {RunLabelRules.ZoomHideBelowM:0} and {RunLabelRules.ZoomHideAboveM:0} m (full " +
                         $"{RunLabelRules.ZoomFullFromM:0}-{RunLabelRules.ZoomFullToM:0} m, {RunLabelRules.AlphaSteps} alpha steps); hidden while " +
                         "a run is drawn or edited, with the tree tool and in photo mode; rebuilt only when the catalog moves");
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

        // ── the frame ────────────────────────────────────────────────────────────────────────────────────

        protected override void OnUpdate()
        {
            if (m_Disabled || !m_Armed)
                return;
            try
            {
                SkiResortMod.Settings.SkiResortSettings s = Mod.Settings;
                if (s != null && !s.ShowRunNamesOnTheHill)
                {
                    if (m_Labels.Count != 0)
                        ReleaseAll();                       // the option was switched off: give the memory back
                    return;
                }
                if (m_Rendering.hideOverlay)
                    return;                                 // photo mode / screenshot, as the district names

                float zoom = ReadZoom();
                int step = RunLabelRules.AlphaStep(RunLabelRules.FadeAlpha(zoom));
                if (step == 0)
                {
                    m_Step = 0;
                    return;
                }
                if (ToolHidesAll())
                    return;
                Camera cam = m_Camera.activeCamera;
                if (cam == null)
                    return;

                int revision = m_Catalog.Revision;
                long rebuilds = m_Catalog.Rebuilds;
                if (revision != m_BuiltRevision || rebuilds != m_BuiltRebuilds)
                    Rebuild(revision, rebuilds);
                if (m_Labels.Count == 0)
                    return;
                if (step != m_Step)
                {
                    m_Step = step;
                    ApplyAlpha(step);
                }
                CollectEditing();
                Draw(cam, zoom);
            }
            catch (Exception e)
            {
                HandleException(e);
            }
        }

        // ── the gates ────────────────────────────────────────────────────────────────────────────────────

        /// <summary>The area tool holding one of our run prefabs (drawing) or the tree tool: no labels at all.</summary>
        private bool ToolHidesAll()
        {
            ToolBaseSystem tool = m_ToolSystem.activeTool;
            if (tool == null)
                return false;
            if (tool == m_AreaTool)
            {
                PrefabBase prefab = m_AreaTool.prefab;
                if (!ReferenceEquals(prefab, m_AreaPrefabRef))
                {
                    m_AreaPrefabRef = prefab;
                    m_AreaPrefabIsRun = !ReferenceEquals(prefab, null) &&
                                        m_PrefabSystem.TryGetEntity(prefab, out Entity e) && m_Ownership.IsRunPrefab(e);
                }
                return m_AreaPrefabIsRun;
            }
            return m_GladeTool != null && m_GladeTool.IsActive;
        }

        /// <summary>Runs with a Temp copy (an edit of an existing run by the area tool or the bulldozer): their labels step aside
        /// for the edit. Only walked while the area tool is active and a Temp surface exists.</summary>
        private void CollectEditing()
        {
            m_Editing.Clear();
            if (m_ToolSystem.activeTool == null || m_TempQuery.IsEmptyIgnoreFilter)
                return;
            NativeArray<Temp> temps = m_TempQuery.ToComponentDataArray<Temp>(Allocator.Temp);
            for (int i = 0; i < temps.Length; i++)
            {
                if (temps[i].m_Original != Entity.Null)
                    m_Editing.Add(temps[i].m_Original);
            }
            temps.Dispose();
        }

        // ── the cache ────────────────────────────────────────────────────────────────────────────────────

        private void Rebuild(int revision, long rebuilds)
        {
            m_BuiltRevision = revision;
            m_BuiltRebuilds = rebuilds;
            Rebuilds++;
            m_Seen.Clear();
            m_Draw.Clear();
            TerrainHeightData hd = m_Terrain.GetHeightData();
            int n = m_Catalog.RowCount;
            for (int i = 0; i < n; i++)
            {
                SkiRunCatalogSystem.RunRow row = m_Catalog.GetRow(i);
                if (row.Entity == Entity.Null || !IsFinite(row.CenterX) || !IsFinite(row.CenterZ))
                    continue;
                string text = RunLabelRules.CleanText(row.Name);
                if (text.Length == 0)
                    continue;
                m_Seen.Add(row.Entity);
                float3 pos = new float3(row.CenterX, 0f, row.CenterZ);
                pos.y = TerrainUtils.SampleHeight(ref hd, pos) + Tuning.LiftM;
                float extent = RunLabelRules.Extent(row.OutlineXZ);
                if (!m_Labels.TryGetValue(row.Entity, out Label label))
                {
                    if (m_Labels.Count >= Tuning.MaxLabels)
                        continue;
                    label = new Label { Run = row.Entity };
                    m_Labels[row.Entity] = label;
                }
                label.Pos = pos;
                label.ExtentM = extent;
                if (label.Parts.Count == 0 || label.Text != text || label.Band != row.Band)
                {
                    label.Text = text;
                    label.Band = row.Band;
                    BuildMeshes(label);
                    label.AppliedStep = -1;
                    m_Step = -1;                            // the new meshes carry alpha 255: re-apply the current step
                }
            }
            m_Gone.Clear();
            foreach (KeyValuePair<Entity, Label> kv in m_Labels)
            {
                if (!m_Seen.Contains(kv.Key))
                    m_Gone.Add(kv.Key);
            }
            for (int i = 0; i < m_Gone.Count; i++)
            {
                DisposeLabel(m_Labels[m_Gone[i]]);
                m_Labels.Remove(m_Gone[i]);
            }
            foreach (KeyValuePair<Entity, Label> kv in m_Labels)
                m_Draw.Add(kv.Value);
        }

        /// <summary>The game's TextMeshPro text mesh -> one Mesh per atlas page, vertex colours = the band colour. Mirrors
        /// OverlayRenderSystem.GetTextRenderItems / FillMeshFromTMPMeshInfo (:706-735, :674-698).</summary>
        private void BuildMeshes(Label label)
        {
            DisposeParts(label);
            TextMeshPro tmp = m_Overlay.GetTextMesh();                    // OverlayRenderSystem.cs:641
            Color keepColour = tmp.color;
            bool keepRich = tmp.richText;
            try
            {
                RunLabelRules.BandRgb(label.Band, out byte r, out byte g, out byte b);
                tmp.richText = false;
                tmp.color = new Color32(r, g, b, 255);
                tmp.rectTransform.sizeDelta = new Vector2(Tuning.SizeW, Tuning.SizeH);
                tmp.fontSize = Tuning.FontSize;
                tmp.alignment = TextAlignmentOptions.Center;
                TMP_TextInfo info = tmp.GetTextInfo(label.Text);
                float minX = float.MaxValue, maxX = float.MinValue;
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
                    }
                    var mesh = new Mesh { name = "SkiRunLabel " + label.Text };
                    mesh.vertices = vertices;
                    mesh.uv = uv0;
                    mesh.uv2 = uv2;
                    mesh.colors32 = colours;
                    mesh.triangles = triangles;
                    mesh.RecalculateBounds();
                    label.Parts.Add(new Part { Mesh = mesh, Material = material, Colours = colours, Scratch = new Color32[vc] });
                }
                label.TextWidth = maxX > minX ? maxX - minX : 0f;
                MeshBuilds++;
            }
            finally
            {
                tmp.color = keepColour;                                   // the shared mesh also draws the water-source names
                tmp.richText = keepRich;
            }
        }

        /// <summary>A material per font atlas texture: the game's overlay text material with the atlas parameters copied from TMP's.</summary>
        private Material MaterialFor(Material tmpMaterial)
        {
            if (tmpMaterial == null)
                return null;
            Texture atlas = tmpMaterial.mainTexture;
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
                        Mod.Log.Warn("RunLabels: the overlay text material is not available — no run names until the next load");
                    }
                    return null;
                }
                m_TextMaterialSource = config.m_TextMaterial;
            }
            var material = new Material(m_TextMaterialSource) { name = "SkiRunLabel text" };    // as GetOverlayTextMaterial :1147-1153
            m_Overlay.CopyFontAtlasParameters(tmpMaterial, material);                           // :667-672
            m_Materials[atlas] = material;
            return material;
        }

        /// <summary>The fade: vertex alpha = step / AlphaSteps of every label. Only when the step moves.</summary>
        private void ApplyAlpha(int step)
        {
            byte a = RunLabelRules.AlphaByte(step);
            foreach (Label label in m_Labels.Values)
            {
                if (label.AppliedStep == step)
                    continue;
                label.AppliedStep = step;
                for (int p = 0; p < label.Parts.Count; p++)
                {
                    Part part = label.Parts[p];
                    for (int k = 0; k < part.Colours.Length; k++)
                    {
                        Color32 c = part.Colours[k];
                        c.a = (byte)(c.a * a / 255);
                        part.Scratch[k] = c;
                    }
                    part.Mesh.colors32 = part.Scratch;
                }
            }
        }

        // ── the draw ─────────────────────────────────────────────────────────────────────────────────────

        private void Draw(Camera cam, float zoom)
        {
            float3 camPos = m_Camera.position;
            float3 pivot = Pivot(camPos);
            float3 right = cam.transform.right;
            quaternion q = AreaUtils.CalculateLabelRotation(right);                       // AreaUtils.cs:115-119
            Quaternion rotation = new Quaternion(q.value.x, q.value.y, q.value.z, q.value.w);
            int drawn = 0;
            for (int i = 0; i < m_Draw.Count; i++)
            {
                Label label = m_Draw[i];
                if (m_Editing.Contains(label.Run))
                    continue;
                if (!RunLabelRules.InRange(label.Pos.x - pivot.x, label.Pos.z - pivot.z, zoom))
                    continue;
                float scale = AreaUtils.CalculateLabelScale(camPos, label.Pos) * Tuning.ScaleK;   // :126-129
                scale = RunLabelRules.FitScale(scale, label.TextWidth, label.ExtentM);
                Matrix4x4 matrix = Matrix4x4.TRS(new Vector3(label.Pos.x, label.Pos.y, label.Pos.z), rotation, new Vector3(scale, scale, scale));
                for (int p = 0; p < label.Parts.Count; p++)
                {
                    Part part = label.Parts[p];
                    Graphics.DrawMesh(part.Mesh, matrix, part.Material, 0, cam, 0, null, false, false);
                }
                drawn++;
            }
            LastDrawn = drawn;
            Draws++;
        }

        private float3 Pivot(float3 fallback)
        {
            try
            {
                IGameCameraController controller = m_Camera.activeCameraController;
                if (controller != null)
                {
                    Vector3 p = controller.pivot;
                    return new float3(p.x, p.y, p.z);
                }
            }
            catch (Exception)
            {
                // a controller being torn down: the camera position
            }
            return fallback;
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

        // ── lifetime ─────────────────────────────────────────────────────────────────────────────────────

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        private static void DisposeParts(Label label)
        {
            for (int p = 0; p < label.Parts.Count; p++)
            {
                if (label.Parts[p].Mesh != null)
                    UnityEngine.Object.Destroy(label.Parts[p].Mesh);
            }
            label.Parts.Clear();
        }

        private static void DisposeLabel(Label label) => DisposeParts(label);

        private void ReleaseAll()
        {
            foreach (Label label in m_Labels.Values)
                DisposeLabel(label);
            m_Labels.Clear();
            m_Draw.Clear();
            m_Editing.Clear();
            foreach (Material m in m_Materials.Values)
            {
                if (m != null)
                    UnityEngine.Object.Destroy(m);
            }
            m_Materials.Clear();
            m_TextMaterialSource = null;
            m_BuiltRevision = int.MinValue;
            m_BuiltRebuilds = -1;
            m_Step = -1;
        }

        private void HandleException(Exception e)
        {
            m_Disabled = true;
            if (!m_ErrorLogged)
            {
                m_ErrorLogged = true;
                Mod.Log.Error($"!!! {nameof(SkiRunLabelSystem)} EXCEPTION — the run names on the hill are off until the next game load " +
                              $"(everything else keeps working). Please report this stack:\n{e}");
            }
            try
            {
                ReleaseAll();
            }
            catch (Exception)
            {
                // already in the failure path; the "!!!" line carries the cause
            }
        }
    }
}
