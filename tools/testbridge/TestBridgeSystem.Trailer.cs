// Ops for filmed takes (README.md, "Filming"): `campath` (eased camera moves on a spline, by real frame time), `glide` (eased cursor
// motion), `ui` (the whole UI view off for a take), `terrain` (small brush edits with a bit-exact snapshot, an undo and a log) and
// `wind` (the wind simulation's constant wind). `campath`, `glide` and `ui` are sequence steps too, so a take is one `seq`.
// Nothing here writes game state except `terrain` (the terrain tool's own brush, driven by real input; the undo writes the snapshot
// back into the base heightmap, as the ski mod's HeightmapWriter does) and `wind` (a saved value: put it back before a save).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Colossal.Mathematics;
using Game;
using Game.Prefabs;
using Game.Rendering;
using Game.Rendering.CinematicCamera;
using Game.SceneFlow;
using Game.Simulation;
using Game.Tools;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace SkiTestBridge
{
    public partial class TestBridgeSystem
    {
        // ------------------------------------------------------------------------------------------------------------------ campath

        private const float kMaxPathSeconds = 600f;
        private const float kHitchSeconds = 0.025f;   // a frame longer than 25 ms reads as a hitch at 60 fps

        private sealed class CamKey
        {
            public float t;
            public float3 pos;        // Photo Mode: the camera. Normal camera: the pivot.
            public float3 at;         // Photo Mode: the look-at point
            public bool hasAt;
            public float yaw, pitch;  // Photo Mode without `at`. Normal camera: yaw and tilt.
            public float zoom;        // normal camera
        }

        private sealed class CamProp
        {
            public string id;
            public float from, to;
        }

        private sealed class CamPath
        {
            public bool photoMode;
            public float seconds;
            public float fixedDt;
            public string ease;
            public bool snap;
            public List<CamKey> keys = new List<CamKey>();
            public List<CamProp> props = new List<CamProp>();
            public Entity follow;
            public float3 followOffset;
            public bool attach;
            public float3 followStart;
            public float elapsed;
            public int frames;
            public int hitches;
            public float maxDt;
            public double sumDt;
            public bool started;
            public bool inputWasEnabled;
        }

        private CamPath m_Path;

        /// <summary>Op "campath": starts a one-step sequence, so `seqStatus` reports it and `abort` stops it.</summary>
        private JObject CamPathCommand(JObject args)
        {
            ParsePath(args);   // fail here, not on a later frame
            var step = (JObject)args.DeepClone();
            step["op"] = "campath";
            return StartSequence(new JObject { ["steps"] = new JArray(step) });
        }

        private CamPath ParsePath(JObject step)
        {
            var path = new CamPath
            {
                seconds = (float?)step["seconds"] ?? 5f,
                ease = (string)step["ease"] ?? "inOut",
                snap = (bool?)step["snap"] ?? true,
            };
            if (!(path.seconds > 0f) || path.seconds > kMaxPathSeconds)
                throw new ArgumentException($"campath: seconds must be over 0 and at most {kMaxPathSeconds}");
            EaseOf(path.ease, 0.5f);
            if (step["fps"] != null)
            {
                float fps = (float)step["fps"];
                if (fps < 10f || fps > 240f)
                    throw new ArgumentException("campath: fps must be 10 to 240");
                path.fixedDt = 1f / fps;
            }
            bool cinematic = m_Camera.cinematicCameraController != null
                && m_Camera.activeCameraController == (IGameCameraController)m_Camera.cinematicCameraController;
            string mode = (string)step["mode"] ?? (cinematic ? "pm" : "game");
            if (mode != "pm" && mode != "game")
                throw new ArgumentException("campath: mode is \"pm\" (Photo Mode camera) or \"game\" (normal camera)");
            path.photoMode = mode == "pm";
            if (path.photoMode && !cinematic)
                throw new InvalidOperationException("campath: Photo Mode is off: {op:\"pm\",on:true} first, or mode \"game\"");
            if (!path.photoMode && m_Camera.gamePlayController == null)
                throw new InvalidOperationException("campath: no gameplay camera");

            if (!(step["keys"] is JArray keys) || keys.Count < 2)
                throw new ArgumentException("campath: keys needs at least 2 entries");
            int n = keys.Count;
            for (int i = 0; i < n; i++)
            {
                var k = (JObject)keys[i];
                var key = new CamKey { t = (float?)k["t"] ?? (float)i / (n - 1) };
                if (path.photoMode)
                {
                    key.pos = Vec3(k["pos"], "campath key pos");
                    if (k["at"] != null)
                    {
                        key.at = Vec3(k["at"], "campath key at");
                        key.hasAt = true;
                    }
                    else
                    {
                        if (k["yaw"] == null || k["pitch"] == null)
                            throw new ArgumentException("campath: a Photo Mode key needs `at` or `yaw` and `pitch`");
                        key.yaw = (float)k["yaw"];
                        key.pitch = (float)k["pitch"];
                    }
                }
                else
                {
                    if (k["wx"] == null || k["wz"] == null || k["zoom"] == null || k["yaw"] == null || k["tilt"] == null)
                        throw new ArgumentException("campath: a normal-camera key needs wx, wz, zoom, yaw and tilt (wy is optional)");
                    key.pos = WorldPoint(k);
                    key.zoom = (float)k["zoom"];
                    key.yaw = (float)k["yaw"];
                    key.pitch = (float)k["tilt"];
                }
                path.keys.Add(key);
            }
            if (path.photoMode)
            {
                bool at0 = path.keys[0].hasAt;
                foreach (CamKey key in path.keys)
                    if (key.hasAt != at0)
                        throw new ArgumentException("campath: give `at` on every key or on none");
            }
            if (Math.Abs(path.keys[0].t) > 1e-5f || Math.Abs(path.keys[n - 1].t - 1f) > 1e-5f)
                throw new ArgumentException("campath: key t runs from 0 on the first key to 1 on the last");
            for (int i = 1; i < n; i++)
                if (path.keys[i].t <= path.keys[i - 1].t)
                    throw new ArgumentException("campath: key t must rise");

            if (step["props"] is JObject props)
            {
                if (!path.photoMode)
                    throw new ArgumentException("campath: props are Photo Mode properties and need mode \"pm\"");
                var render = World.GetOrCreateSystemManaged<PhotoModeRenderSystem>();
                foreach (var row in props.Properties())
                {
                    if (!(row.Value is JArray pair) || pair.Count != 2)
                        throw new ArgumentException($"campath: props.{row.Name} must be [from, to]");
                    if (FindPhotoProperty(render, row.Name) == null)
                        throw new ArgumentException($"no Photo Mode property '{row.Name}'");
                    path.props.Add(new CamProp { id = row.Name, from = (float)pair[0], to = (float)pair[1] });
                }
            }
            if (step["follow"] is JObject follow)
            {
                if (!path.photoMode)
                    throw new ArgumentException("campath: follow needs mode \"pm\"");
                path.follow = ParseEntity((string)follow["entity"]);
                if (!EntityManager.Exists(path.follow) || !TryEntityPosition(path.follow, out _))
                    throw new ArgumentException($"campath: follow entity {(string)follow["entity"]} does not exist or has no position");
                path.followOffset = follow["offset"] != null ? Vec3(follow["offset"], "campath follow offset") : new float3(0f, 1.5f, 0f);
                path.attach = (bool?)follow["attach"] ?? false;
            }
            return path;
        }

        /// <summary>The step: false while the path runs. One camera pose per rendered frame, by unscaled frame time (or 1 / fps).</summary>
        private bool RunCamPath(Sequence s, JObject step)
        {
            if (s.phase == 0)
            {
                m_Path = ParsePath(step);
                s.phase = 1;
            }
            CamPath p = m_Path;
            float dt = 0f;
            if (!p.started)
            {
                p.started = true;
                if (p.follow != Entity.Null)
                    TryEntityPosition(p.follow, out p.followStart);
                if (p.photoMode)
                {
                    p.inputWasEnabled = m_Camera.cinematicCameraController.inputEnabled;
                    m_Camera.cinematicCameraController.inputEnabled = false;
                }
            }
            else
            {
                float real = UnityEngine.Time.unscaledDeltaTime;
                dt = p.fixedDt > 0f ? p.fixedDt : real;
                p.frames++;
                p.sumDt += real;
                p.maxDt = Math.Max(p.maxDt, real);
                if (real > kHitchSeconds)
                    p.hitches++;
            }
            p.elapsed += dt;
            float u = math.saturate(p.elapsed / p.seconds);
            ApplyPath(p, EaseOf(p.ease, u));
            if (u < 1f)
                return false;

            EndPath();
            s.results.Add(new JObject
            {
                ["step"] = s.index,
                ["campath"] = new JObject
                {
                    ["frames"] = p.frames,
                    ["seconds"] = Math.Round(p.sumDt, 3),
                    ["fps"] = p.sumDt > 0 ? Math.Round(p.frames / p.sumDt, 2) : 0,
                    ["maxFrameMs"] = Math.Round(p.maxDt * 1000f, 2),
                    ["hitches"] = p.hitches,
                },
            });
            Next(s, (int?)step["after"] ?? 0);
            return true;
        }

        /// <summary>Gives the Photo Mode camera its input back. Runs when a path ends and when its sequence ends for any reason.</summary>
        private void EndPath()
        {
            CamPath p = m_Path;
            m_Path = null;
            if (p != null && p.started && p.photoMode && m_Camera.cinematicCameraController != null)
                m_Camera.cinematicCameraController.inputEnabled = p.inputWasEnabled;
        }

        private void ApplyPath(CamPath p, float u)
        {
            // The segment of u and the place inside it.
            int i = 0;
            while (i < p.keys.Count - 2 && u > p.keys[i + 1].t)
                i++;
            CamKey a = p.keys[i], b = p.keys[i + 1];
            float f = math.saturate((u - a.t) / (b.t - a.t));
            CamKey before = p.keys[Math.Max(i - 1, 0)], after = p.keys[Math.Min(i + 2, p.keys.Count - 1)];
            float3 pos = CatmullRom(before.pos, a.pos, b.pos, after.pos, f);

            if (!p.photoMode)
            {
                var camera = m_Camera.gamePlayController;
                if (m_Camera.activeCameraController != (IGameCameraController)camera)
                {
                    camera.TryMatchPosition(m_Camera.activeCameraController);
                    m_Camera.activeCameraController = camera;
                }
                camera.pivot = (Vector3)pos;
                // Zoom is a distance: blend its logarithm, so a push-in keeps a steady rate on screen.
                camera.zoom = math.exp(math.lerp(math.log(a.zoom), math.log(b.zoom), f));
                camera.angle = new float2(LerpDegrees(a.yaw, b.yaw, f), math.lerp(a.pitch, b.pitch, f));
                return;
            }

            float3 shift = float3.zero;
            float3 target = default;
            bool hasTarget = false;
            if (p.follow != Entity.Null && TryEntityPosition(p.follow, out float3 subject))
            {
                if (p.attach)
                    shift = subject - p.followStart;
                target = subject + p.followOffset;
                hasTarget = true;
            }
            pos += shift;
            var cam = m_Camera.cinematicCameraController;
            Vector3 euler;
            if (!hasTarget && a.hasAt)
            {
                target = CatmullRom(before.at, a.at, b.at, after.at, f) + shift;
                hasTarget = true;
            }
            if (hasTarget)
            {
                Quaternion q = Quaternion.LookRotation(((Vector3)(target - pos)).normalized, Vector3.up);
                euler = new Vector3(q.eulerAngles.x, q.eulerAngles.y, 0f);
            }
            else
            {
                euler = new Vector3(math.lerp(a.pitch, b.pitch, f), LerpDegrees(a.yaw, b.yaw, f), 0f);
            }
            cam.position = (Vector3)pos;
            cam.rotation = euler;
            if (p.snap)
                cam.SnapToTarget();
            if (p.props.Count > 0)
            {
                var render = World.GetOrCreateSystemManaged<PhotoModeRenderSystem>();
                foreach (CamProp prop in p.props)
                {
                    PhotoModeProperty property = FindPhotoProperty(render, prop.id);
                    if (property == null)
                        continue;
                    property.setEnabled?.Invoke(true);
                    property.setValue?.Invoke(math.lerp(prop.from, prop.to, u));
                }
            }
        }

        private static PhotoModeProperty FindPhotoProperty(PhotoModeRenderSystem render, string id)
        {
            foreach (var pair in render.photoModeProperties)
                if (pair.Key == id)
                    return pair.Value;
            return null;
        }

        /// <summary>Where the game draws a moving object this frame (InterpolatedTransform), else its Transform.</summary>
        private bool TryEntityPosition(Entity entity, out float3 position)
        {
            position = default;
            if (!EntityManager.Exists(entity))
                return false;
            if (EntityManager.HasComponent<InterpolatedTransform>(entity))
            {
                position = EntityManager.GetComponentData<InterpolatedTransform>(entity).m_Position;
                return true;
            }
            if (EntityManager.HasComponent<Game.Objects.Transform>(entity))
            {
                position = EntityManager.GetComponentData<Game.Objects.Transform>(entity).m_Position;
                return true;
            }
            return false;
        }

        private static float3 CatmullRom(float3 p0, float3 p1, float3 p2, float3 p3, float f)
        {
            float f2 = f * f, f3 = f2 * f;
            return 0.5f * (2f * p1 + (p2 - p0) * f + (2f * p0 - 5f * p1 + 4f * p2 - p3) * f2 + (3f * p1 - p0 - 3f * p2 + p3) * f3);
        }

        private static float LerpDegrees(float from, float to, float f)
        {
            float delta = ((to - from) % 360f + 540f) % 360f - 180f;
            return from + delta * f;
        }

        /// <summary>linear, in (slow start), out (slow end), inOut (smoothstep), inOut2 (smootherstep: the softest start and end).</summary>
        private static float EaseOf(string ease, float u)
        {
            switch (ease)
            {
                case "linear": return u;
                case "in": return u * u;
                case "out": return 1f - (1f - u) * (1f - u);
                case "inOut": return u * u * (3f - 2f * u);
                case "inOut2": return u * u * u * (u * (u * 6f - 15f) + 10f);
                default: throw new ArgumentException($"unknown ease '{ease}': linear, in, out, inOut, inOut2");
            }
        }

        private static float3 Vec3(JToken token, string what)
        {
            if (!(token is JArray a) || a.Count != 3)
                throw new ArgumentException($"{what} must be [x, y, z]");
            return new float3((float)a[0], (float)a[1], (float)a[2]);
        }

        // -------------------------------------------------------------------------------------------------------------------- glide

        private float m_GlideElapsed;

        /// <summary>Step "glide": the cursor travels to client pixels (x, y) or a world point (wx, wz, optional wy) in `seconds` of real
        /// time, eased. A world target is projected again on every frame, so it holds under a moving camera.</summary>
        private bool RunGlide(Sequence s, JObject step, int after)
        {
            float seconds = (float?)step["seconds"] ?? 1f;
            if (!(seconds > 0f) || seconds > 60f)
                throw new ArgumentException("glide: seconds must be over 0 and at most 60");
            string ease = (string)step["ease"] ?? "inOut";
            float2 target = MoveTarget(step);
            s_AimedAtWorld = step["wx"] != null;
            if (s.phase == 0)
            {
                if (!NativeInput.TryGetCursorClient(out int x, out int y))
                    throw new InvalidOperationException("the game window does not have focus");
                s.start = new float2(x, y);
                s.phase = 1;
                m_GlideElapsed = 0f;
            }
            else
            {
                m_GlideElapsed += UnityEngine.Time.unscaledDeltaTime;
            }
            float u = math.saturate(m_GlideElapsed / seconds);
            float2 position = math.lerp(s.start, target, EaseOf(ease, u));
            NativeInput.MoveClient((int)math.round(position.x), (int)math.round(position.y));
            if (u < 1f)
                return false;
            Next(s, after);
            return true;
        }

        // ----------------------------------------------------------------------------------------------------------------------- ui

        private const float kUiMaxHiddenSeconds = 900f;
        private bool m_UiHidden;
        private float m_UiDeadline;

        /// <summary>Op and step "ui": {hidden: true | false, seconds}. Hides the whole UI view (the `photo` op's own switch), so OBS
        /// records the picture alone. The page does not run while the view is off, so the view comes back by itself: when the
        /// sequence that hid it ends, and after `seconds` (default 60) in any case.</summary>
        private JObject UiCommand(JObject args)
        {
            var ui = GameManager.instance.userInterface;
            if (ui == null)
                throw new InvalidOperationException("no UI view");
            if (args["hidden"] != null)
            {
                bool hidden = (bool)args["hidden"];
                float seconds = (float?)args["seconds"] ?? 60f;
                if (hidden && (!(seconds > 0f) || seconds > kUiMaxHiddenSeconds))
                    throw new ArgumentException($"ui: seconds must be over 0 and at most {kUiMaxHiddenSeconds}");
                ui.view.enabled = !hidden;
                m_UiHidden = hidden;
                m_UiDeadline = UnityEngine.Time.realtimeSinceStartup + seconds;
            }
            return new JObject { ["hidden"] = !ui.view.enabled };
        }

        private void ShowUi()
        {
            if (!m_UiHidden)
                return;
            m_UiHidden = false;
            var ui = GameManager.instance.userInterface;
            if (ui != null)
                ui.view.enabled = true;
        }

        /// <summary>Every frame: the deadline of a hidden UI, and the clean-up after a sequence that ended while it owned the camera
        /// or the UI (an error, an abort).</summary>
        private void TickTrailer()
        {
            bool running = m_Sequence != null && !m_Sequence.done;
            if (!running && m_Path != null)
                EndPath();
            if (m_UiHidden && (UnityEngine.Time.realtimeSinceStartup > m_UiDeadline || (!running && m_UiHiddenBySequence)))
                ShowUi();
            if (!running)
                m_UiHiddenBySequence = false;
        }

        private bool m_UiHiddenBySequence;

        // ------------------------------------------------------------------------------------------------------------------ terrain

        private const float kTerrainMaxSize = 10f;
        private const float kTerrainMinSize = 1f;
        private const float kTerrainMaxStrength = 0.35f;
        private const int kTerrainMaxTexels = 512 * 512;

        private sealed class TerrainEdit
        {
            public string id;
            public string prefab;
            public float size, strength;
            public float? height;
            public float x, z;
            public int4 rect;         // x, y, width, height in heightmap texels
            public ushort[] before;
            public string snapDir;
            public string log;
            public string note;
        }

        private TerrainEdit m_TerrainEdit;

        /// <summary>Op "terrain": action begin | end | cancel | undo | read. The stroke between begin and end is real input (move to the
        /// point, hold the left button a few frames), so the game's own brush does the edit. Limits (the shoot's rule): brush size
        /// 1 to 10, strength 0.35 or less.</summary>
        private JObject TerrainCommand(JObject args)
        {
            string action = (string)args["action"] ?? "read";
            switch (action)
            {
                case "begin": return TerrainBegin(args);
                case "end": return TerrainEnd();
                case "cancel":
                    m_TerrainEdit = null;
                    m_Tools.ActivatePrefabTool(null);
                    return new JObject { ["canceled"] = true };
                case "undo": return TerrainUndo(args);
                case "read":
                    return new JObject
                    {
                        ["pending"] = m_TerrainEdit?.id,
                        ["map"] = MapInfo(),
                        ["tool"] = m_Tools.activeTool is TerrainToolSystem t
                            ? new JObject { ["prefab"] = t.prefab?.name, ["size"] = t.brushSize, ["strength"] = t.brushStrength, ["height"] = t.brushHeight }
                            : null,
                    };
                default: throw new ArgumentException($"terrain: unknown action '{action}'");
            }
        }

        private JObject TerrainBegin(JObject args)
        {
            if (m_TerrainEdit != null)
                throw new InvalidOperationException($"terrain: edit {m_TerrainEdit.id} is open: end or cancel it first");
            if (GameManager.instance.gameMode != GameMode.Game)
                throw new InvalidOperationException("terrain: only in a loaded city");
            var edit = new TerrainEdit
            {
                prefab = (string)args["prefab"] ?? throw new ArgumentException("terrain: prefab is the terraforming prefab's name (see {op:\"prefabs\",match:\"Terrain\"})"),
                size = (float?)args["size"] ?? throw new ArgumentException("terrain: size is required (1 to 10)"),
                strength = (float?)args["strength"] ?? throw new ArgumentException("terrain: strength is required (0.35 or less)"),
                height = (float?)args["height"],
                x = (float)args["wx"],
                z = (float)args["wz"],
                snapDir = (string)args["snapDir"],
                log = (string)args["log"],
                note = (string)args["note"] ?? "",
            };
            if (edit.size < kTerrainMinSize || edit.size > kTerrainMaxSize)
                throw new ArgumentException($"terrain: size {edit.size} is outside {kTerrainMinSize} to {kTerrainMaxSize}");
            if (!(edit.strength > 0f) || edit.strength > kTerrainMaxStrength)
                throw new ArgumentException($"terrain: strength {edit.strength} is outside 0 to {kTerrainMaxStrength}");
            if (edit.snapDir == null || !Path.IsPathRooted(edit.snapDir) || edit.log == null || !Path.IsPathRooted(edit.log))
                throw new ArgumentException("terrain: snapDir and log must be absolute paths");

            float radius = (float?)args["r"] ?? edit.size * 2f + 12f;
            edit.rect = TexelRect(edit.x, edit.z, radius);
            edit.before = ReadHeights(edit.rect);
            edit.id = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
            Directory.CreateDirectory(edit.snapDir);
            WriteSnapshot(Path.Combine(edit.snapDir, edit.id + "-before.json"), edit.rect, edit.before);

            if (!m_Prefabs.TryGetPrefab(FindPrefabEntity(edit.prefab), out PrefabBase prefab) || !(prefab is TerraformingPrefab))
                throw new ArgumentException($"terrain: '{edit.prefab}' is not a terraforming prefab");
            if (!m_Tools.ActivatePrefabTool(prefab) || !(m_Tools.activeTool is TerrainToolSystem tool))
                throw new InvalidOperationException($"terrain: the terrain tool did not take '{edit.prefab}'");
            tool.brushSize = edit.size;
            tool.brushStrength = edit.strength;
            if (edit.height != null)
                tool.brushHeight = edit.height.Value;
            m_TerrainEdit = edit;
            return new JObject
            {
                ["id"] = edit.id,
                ["rect"] = new JArray(edit.rect.x, edit.rect.y, edit.rect.z, edit.rect.w),
                ["tool"] = new JObject { ["prefab"] = tool.prefab?.name, ["type"] = tool.prefab?.m_Type.ToString(), ["size"] = tool.brushSize, ["strength"] = tool.brushStrength, ["height"] = tool.brushHeight },
                ["before"] = Path.Combine(edit.snapDir, edit.id + "-before.json"),
            };
        }

        private JObject TerrainEnd()
        {
            TerrainEdit edit = m_TerrainEdit ?? throw new InvalidOperationException("terrain: no open edit");
            ushort[] after = ReadHeights(edit.rect);
            WriteSnapshot(Path.Combine(edit.snapDir, edit.id + "-after.json"), edit.rect, after);
            JObject stats = Compare(edit.before, after, edit.rect, out string cells);
            m_TerrainEdit = null;
            m_Tools.ActivatePrefabTool(null);
            AppendTerrainLog(edit.log, string.Format(CultureInfo.InvariantCulture,
                "| {0} | {1} | {2:F1}, {3:F1} | {4} | {5:F0} | {6:F0} % | {7} | {8} | {9} | {10} | {11} |",
                edit.id, m_Time.GetCurrentDateTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), edit.x, edit.z, edit.prefab, edit.size,
                edit.strength * 100f, edit.height != null ? edit.height.Value.ToString("F2", CultureInfo.InvariantCulture) : "-",
                (string)stats["maxRaise"] + " / " + (string)stats["maxLower"], cells, edit.note.Replace("|", "/"), edit.id + "-before.json"));
            stats["id"] = edit.id;
            stats["after"] = Path.Combine(edit.snapDir, edit.id + "-after.json");
            return stats;
        }

        /// <summary>Writes a snapshot file back (bit-exact, verified by a readback) and runs the game's own change notifications.</summary>
        private JObject TerrainUndo(JObject args)
        {
            string file = (string)args["file"] ?? throw new ArgumentException("terrain undo: file is the -before.json snapshot");
            JObject snap = JObject.Parse(File.ReadAllText(file));
            var r = (JArray)snap["rect"];
            var rect = new int4((int)r[0], (int)r[1], (int)r[2], (int)r[3]);
            JObject now = MapInfo();
            foreach (string key in new[] { "width", "height", "heightScale", "heightOffset" })
                if (Math.Abs((double)now[key] - (double)snap["map"][key]) > 1e-3)
                    throw new InvalidOperationException($"terrain undo: the snapshot is from another map ({key} {snap["map"][key]} vs {now[key]})");
            byte[] bytes = Convert.FromBase64String((string)snap["data"]);
            var data = new ushort[bytes.Length / 2];
            Buffer.BlockCopy(bytes, 0, data, 0, bytes.Length);
            if (data.Length != rect.z * rect.w)
                throw new InvalidOperationException("terrain undo: the snapshot's data does not match its rect");
            ushort[] current = ReadHeights(rect);
            JObject stats = Compare(current, data, rect, out string cells);
            int mismatches = WriteHeights(rect, data);
            stats["mismatches"] = mismatches;
            if ((string)args["log"] is string log && Path.IsPathRooted(log))
            {
                AppendTerrainLog(log, string.Format(CultureInfo.InvariantCulture, "| UNDO | {0} | rect ({1}, {2}) {3} x {4} | - | - | - | - | {5} | {6} | {7} | {8} |",
                    m_Time.GetCurrentDateTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), rect.x, rect.y, rect.z, rect.w,
                    (string)stats["maxRaise"] + " / " + (string)stats["maxLower"], cells,
                    mismatches == 0 ? "restored bit-exact" : mismatches + " texels did not land", Path.GetFileName(file)));
            }
            return stats;
        }

        private JObject MapInfo()
        {
            Texture tex = m_Terrain.heightmap;
            if (!(tex is RenderTexture rt) || !rt.IsCreated() || rt.graphicsFormat != GraphicsFormat.R16_UNorm)
                throw new InvalidOperationException("terrain: the heightmap is not a created R16_UNorm RenderTexture");
            return new JObject
            {
                ["width"] = rt.width,
                ["height"] = rt.height,
                ["offsetX"] = m_Terrain.playableOffset.x,
                ["offsetZ"] = m_Terrain.playableOffset.y,
                ["areaX"] = m_Terrain.playableArea.x,
                ["areaZ"] = m_Terrain.playableArea.y,
                ["heightScale"] = m_Terrain.heightScaleOffset.x,
                ["heightOffset"] = m_Terrain.heightScaleOffset.y,
                ["texelM"] = Math.Round(m_Terrain.playableArea.x / rt.width, 4),
            };
        }

        private int4 TexelRect(float x, float z, float radius)
        {
            JObject map = MapInfo();
            int width = (int)map["width"], height = (int)map["height"];
            float2 cell = m_Terrain.playableArea / new float2(width, height);
            float2 min = (new float2(x - radius, z - radius) - m_Terrain.playableOffset) / cell;
            float2 max = (new float2(x + radius, z + radius) - m_Terrain.playableOffset) / cell;
            int x0 = math.clamp((int)math.floor(min.x), 0, width - 1), z0 = math.clamp((int)math.floor(min.y), 0, height - 1);
            int x1 = math.clamp((int)math.ceil(max.x), 0, width - 1), z1 = math.clamp((int)math.ceil(max.y), 0, height - 1);
            var rect = new int4(x0, z0, x1 - x0 + 1, z1 - z0 + 1);
            if (rect.z * rect.w > kTerrainMaxTexels)
                throw new ArgumentException($"terrain: the snapshot area is {rect.z} x {rect.w} texels, over {kTerrainMaxTexels}");
            return rect;
        }

        private ushort[] ReadHeights(int4 rect)
        {
            AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(m_Terrain.heightmap, 0, rect.x, rect.z, rect.y, rect.w, 0, 1);
            request.WaitForCompletion();
            if (request.hasError)
                throw new InvalidOperationException("terrain: heightmap readback failed");
            NativeArray<ushort> data = request.GetData<ushort>();
            if (data.Length != rect.z * rect.w)
                throw new InvalidOperationException($"terrain: readback gave {data.Length} values, expected {rect.z * rect.w}");
            return data.ToArray();
        }

        /// <summary>The ski mod's HeightmapWriter.Write: a sub-rect copy into the base map, a verify readback, then the vanilla tail.</summary>
        private int WriteHeights(int4 rect, ushort[] data)
        {
            var source = new Texture2D(rect.z, rect.w, GraphicsFormat.R16_UNorm, TextureCreationFlags.None)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            try
            {
                source.SetPixelData(data, 0);
                source.Apply(false, false);
                Graphics.CopyTexture(source, 0, 0, 0, 0, rect.z, rect.w, m_Terrain.heightmap, 0, 0, rect.x, rect.y);
            }
            finally
            {
                UnityEngine.Object.Destroy(source);
            }
            ushort[] back = ReadHeights(rect);
            int mismatches = 0;
            for (int i = 0; i < back.Length; i++)
                if (back[i] != data[i])
                    mismatches++;
            JObject map = MapInfo();
            float2 cell = m_Terrain.playableArea / new float2((int)map["width"], (int)map["height"]);
            var area = new Bounds2(m_Terrain.playableOffset + new float2(rect.x, rect.y) * cell,
                m_Terrain.playableOffset + new float2(rect.x + rect.z, rect.y + rect.w) * cell);
            World.GetOrCreateSystemManaged<WaterSystem>().TerrainWillChangeFromBrush(area);
            m_Terrain.OnAreaChanged(in area);
            m_Terrain.UpdateMinMax(default(Brush), area);
            m_Terrain.TriggerAsyncChange();
            return mismatches;
        }

        private void WriteSnapshot(string path, int4 rect, ushort[] data)
        {
            var bytes = new byte[data.Length * 2];
            Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
            var snap = new JObject
            {
                ["rect"] = new JArray(rect.x, rect.y, rect.z, rect.w),
                ["map"] = MapInfo(),
                ["data"] = Convert.ToBase64String(bytes),
            };
            File.WriteAllText(path, snap.ToString(Newtonsoft.Json.Formatting.None));
        }

        /// <summary>The change from `before` to `after` in meters: the largest raise and lowering, and the changed texel count.</summary>
        private JObject Compare(ushort[] before, ushort[] after, int4 rect, out string cells)
        {
            float step = m_Terrain.heightScaleOffset.x / 65535f;
            int changed = 0, maxUp = 0, maxDown = 0;
            long sum = 0;
            for (int i = 0; i < before.Length; i++)
            {
                int d = after[i] - before[i];
                if (d == 0)
                    continue;
                changed++;
                sum += d;
                maxUp = Math.Max(maxUp, d);
                maxDown = Math.Min(maxDown, d);
            }
            cells = changed + " of " + before.Length;
            return new JObject
            {
                ["rect"] = new JArray(rect.x, rect.y, rect.z, rect.w),
                ["changedTexels"] = changed,
                ["texels"] = before.Length,
                ["maxRaise"] = (maxUp * step).ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture),
                ["maxLower"] = (maxDown * step).ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture),
                ["meanChange"] = changed > 0 ? Math.Round(sum * step / changed, 3) : 0,
            };
        }

        private static void AppendTerrainLog(string path, string row)
        {
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path,
                    "# Terrain log\n\nEvery terrain edit made through the bridge's `terrain` op. Heights in meters. \"Snapshot\" is the bit-exact pre-image in " +
                    "`terrain-snaps/`: `{op:\"terrain\",action:\"undo\",file:<snapshot>}` writes it back.\n\n" +
                    "| Edit | Game time | Position (x, z) | Tool | Size | Strength | Target y | Max raise / lower | Texels changed | Note | Snapshot |\n" +
                    "|---|---|---|---|---|---|---|---|---|---|---|\n");
            }
            File.AppendAllText(path, row + "\n");
        }

        // ---------------------------------------------------------------------------------------------------------------------- fps

        /// <summary>Op "fps": {target} sets Application.targetFrameRate for this run of the game (-1 = no cap), so a take records at
        /// a steady rate. It is not a game setting and nothing is saved.</summary>
        private static JObject FpsCommand(JObject args)
        {
            if (args["target"] != null)
            {
                int target = (int)args["target"];
                if (target != -1 && (target < 24 || target > 240))
                    throw new ArgumentException("fps: target is -1 (no cap) or 24 to 240");
                Application.targetFrameRate = target;
            }
            return new JObject { ["target"] = Application.targetFrameRate, ["vSync"] = QualitySettings.vSyncCount };
        }

        // --------------------------------------------------------------------------------------------------------------------- wind

        /// <summary>Op "wind": {x, y} sets WindSimulationSystem.constantWind (the wind the simulation drifts to; the default is
        /// 0.275, 0.275). The simulation follows it over game days, not minutes (measured: no change at a lift after one game hour).
        /// {x, y, force: true} also writes that velocity into every cell of the simulation, so the wind map shows it at its next
        /// update (512 simulation frames); the simulation then decays it slowly. Both are saved values: put them back, or do not save.
        /// {wx, wz} reads the wind map at a point.</summary>
        private JObject WindCommand(JObject args)
        {
            var simulation = World.GetOrCreateSystemManaged<WindSimulationSystem>();
            if (args["x"] != null || args["y"] != null)
            {
                float2 wind = simulation.constantWind;
                if (args["x"] != null)
                    wind.x = (float)args["x"];
                if (args["y"] != null)
                    wind.y = (float)args["y"];
                if (math.length(wind) > 3f)
                    throw new ArgumentException("wind: the length of (x, y) must be 3 or less");
                simulation.constantWind = wind;
                if ((bool?)args["force"] ?? false)
                {
                    NativeArray<WindSimulationSystem.WindCell> cells = simulation.GetCells(out JobHandle running);
                    running.Complete();
                    for (int i = 0; i < cells.Length; i++)
                    {
                        WindSimulationSystem.WindCell cell = cells[i];
                        cell.m_Velocities = new float3(wind.x, wind.y, 0f);
                        cells[i] = cell;
                    }
                }
            }
            var result = new JObject { ["constantWind"] = new JArray(Math.Round(simulation.constantWind.x, 3), Math.Round(simulation.constantWind.y, 3)) };
            if (args["wx"] != null)
            {
                var windSystem = World.GetOrCreateSystemManaged<WindSystem>();
                NativeArray<Wind> map = windSystem.GetMap(true, out JobHandle dependencies);
                dependencies.Complete();
                float3 point = WorldPoint(args);
                float2 w = WindSystem.GetWind(point, map).m_Wind;
                result["at"] = new JArray(Math.Round(w.x, 3), Math.Round(w.y, 3));
                result["strength"] = Math.Round(math.length(w), 3);
            }
            return result;
        }
    }
}
