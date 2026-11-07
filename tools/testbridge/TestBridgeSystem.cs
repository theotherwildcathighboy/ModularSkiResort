// The command surface: one call binding, JSON in and out (README.md). Bindings run on the main thread, so commands read and write
// directly, like the game's own debug menu buttons. Timed input runs as a step sequence in TestBridgeSystem.Steps.cs.

using System;
using Colossal.UI.Binding;
using Game.City;
using Game.Common;
using Game.Input;
using Game.Prefabs;
using Game.Rendering;
using Game.SceneFlow;
using Game.Simulation;
using Game.Tools;
using Game.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SkiTestBridge
{
    public partial class TestBridgeSystem : UISystemBase
    {
        private CameraUpdateSystem m_Camera;
        private TerrainSystem m_Terrain;
        private ToolSystem m_Tools;
        private ToolRaycastSystem m_ToolRaycast;
        private SimulationSystem m_Simulation;
        private TimeSystem m_Time;
        private ClimateSystem m_Climate;
        private CitySystem m_City;
        private PrefabSystem m_Prefabs;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Camera = World.GetOrCreateSystemManaged<CameraUpdateSystem>();
            m_Terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
            m_Tools = World.GetOrCreateSystemManaged<ToolSystem>();
            m_ToolRaycast = World.GetOrCreateSystemManaged<ToolRaycastSystem>();
            m_Simulation = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_Time = World.GetOrCreateSystemManaged<TimeSystem>();
            m_Climate = World.GetOrCreateSystemManaged<ClimateSystem>();
            m_City = World.GetOrCreateSystemManaged<CitySystem>();
            m_Prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            AddBinding(new CallBinding<string, string>("skitest", "cmd", Command));
            WatchErrors();
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();
            TickPerf();
            TickSequence();
            TickMergeProbeUi();
        }

        protected override void OnDestroy()
        {
            UnwatchErrors();
            NativeInput.ReleaseAll();
            base.OnDestroy();
        }

        private string Command(string json)
        {
            try
            {
                JObject args = JObject.Parse(string.IsNullOrEmpty(json) ? "{}" : json);
                JObject result = Run((string)args["op"] ?? "status", args);
                result["ok"] = true;
                return result.ToString(Formatting.None);
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"Command failed: {json}: {e}");
                return new JObject { ["ok"] = false, ["error"] = e.Message }.ToString(Formatting.None);
            }
        }

        private JObject Run(string op, JObject args)
        {
            switch (op)
            {
                case "status": return Status();
                case "hover": return Hover();
                case "project": return ProjectCommand(args);
                case "camera": SetCamera(args); return CameraState();
                case "photo": return PhotoCommand(args);
                case "sim": return SetSimulation(args);
                case "time": m_Time.DebugAdvanceTime((int)args["minutes"]); return TimeState();
                case "money": return AddMoney(args);
                case "climate": return SetClimate(args);
                case "seq": return StartSequence(args);
                case "seqStatus": return SequenceState();
                case "abort": return AbortSequence();
                case "dump": return DumpQuery(args);
                case "entity": return DumpEntity(args);
                case "errors": return Errors(args);
                case "tool": return ActivateTool(args);
                case "prefabs": return FindPrefabs(args);
                case "unlock": return UnlockAll();
                case "milestone": return NextMilestone();
                case "demand": return UnlimitedDemand();
                case "heights": return Heights(args);
                case "perf": return PerfCommand(args);
                case "tiles": return UnlockTiles();
                case "pm": return PhotoMode(args);
                case "comp": return EditComponents(args);
                case "util": return UtilityCommand(args);
                case "offgrid": return OffGridCommand(args);
                case "prefabFlags": return PrefabFlags(args);
                case "autosave": return SetAutoSave(args);
                case "saveTarget": return SaveTarget(args);
                case "u1probe": return U1ProbeCommand(args);
                case "bone": return BoneCommand(args);
                case "mprobe": return MergeProbe(args);
                case "trains": return TrainsCommand(args);
                default: throw new ArgumentException($"unknown op '{op}'");
            }
        }

        private JObject Status(bool includeSequence = true)
        {
            var result = new JObject
            {
                ["bridgeBuild"] = BridgeBuild(),
                ["errorSerial"] = ErrorSerial(),
                ["frame"] = UnityEngine.Time.frameCount,
                ["gameMode"] = GameManager.instance.gameMode.ToString(),
                ["focused"] = NativeInput.FocusedGameWindow() != IntPtr.Zero,
                ["screen"] = new JArray(Screen.width, Screen.height),
                ["hover"] = Hover(),
            };
            if (includeSequence)
                result["seq"] = SequenceState();
            if (m_Camera.gamePlayController != null)
                result["camera"] = CameraState();
            if (m_City.City != Entity.Null)
            {
                result["sim"] = new JObject { ["speed"] = m_Simulation.selectedSpeed, ["frameIndex"] = m_Simulation.frameIndex };
                result["time"] = TimeState();
                result["money"] = EntityManager.GetComponentData<PlayerMoney>(m_City.City).money;
                result["climate"] = ClimateState();
            }
            return result;
        }

        private JObject Hover()
        {
            var result = new JObject { ["mouseOverUI"] = Game.Input.InputManager.instance.mouseOverUI };
            if (NativeInput.TryGetCursorClient(out int x, out int y))
                result["cursor"] = new JArray(x, y);
            if (Mouse.current != null)
            {
                Vector2 p = Mouse.current.position.ReadValue();
                result["gameCursor"] = new JArray(Math.Round(p.x), Math.Round(Screen.height - 1 - p.y));
            }
            if (m_Tools.activeTool != null)
            {
                result["tool"] = m_Tools.activeTool.toolID;
                result["prefab"] = m_Tools.activePrefab?.name;
            }
            if (m_City.City != Entity.Null && m_ToolRaycast.GetRaycastResult(out RaycastResult hit))
            {
                result["hit"] = new JObject
                {
                    ["position"] = Vec(hit.m_Hit.m_HitPosition),
                    ["entity"] = EntityId(hit.m_Hit.m_HitEntity),
                    ["owner"] = EntityId(hit.m_Owner),
                    ["ownerPrefab"] = PrefabNameOf(hit.m_Owner),
                };
            }
            return result;
        }

        private JObject ProjectCommand(JObject args)
        {
            float3 world = WorldPoint(args);
            Vector3 screen = m_Camera.activeCamera.WorldToScreenPoint(world);
            return new JObject
            {
                ["world"] = Vec(world),
                ["client"] = new JArray(Math.Round(screen.x), Math.Round(Screen.height - 1 - screen.y)),
                ["inFront"] = screen.z > 0f,
                ["safe"] = IsSafeTarget(screen),
            };
        }

        /// <summary>The world point of a command: wx, wz and optionally wy. Without wy the point sits on the terrain.</summary>
        private float3 WorldPoint(JObject args)
        {
            float x = (float)args["wx"];
            float z = (float)args["wz"];
            float y = args["wy"] != null ? (float)args["wy"] : TerrainHeight(x, z);
            return new float3(x, y, z);
        }

        private float TerrainHeight(float x, float z)
        {
            TerrainHeightData data = m_Terrain.GetHeightData();
            if (!data.isCreated)
                throw new InvalidOperationException("no terrain loaded");
            return TerrainUtils.SampleHeight(ref data, new float3(x, 0f, z));
        }

        /// <summary>In front of the camera and clear of the 2 % edge band where edge scrolling starts (CameraController.cs:356-367).</summary>
        private static bool IsSafeTarget(Vector3 screen)
        {
            float marginX = Screen.width * 0.03f;
            float marginY = Screen.height * 0.03f;
            return screen.z > 0f && screen.x > marginX && screen.x < Screen.width - marginX
                && screen.y > marginY && screen.y < Screen.height - marginY;
        }

        // `name` is a bare file name (no folder parts, ".png" appended). `dir` is an absolute folder the caller picks. So a test
        // collects its shots where it reports them (default: Screenshots under the game's user data folder). This code creates the
        // folder, so a bad `dir` fails this reply instead of the capture frame. The reply's `path` is the file to read.
        private static JObject PhotoCommand(JObject args)
        {
            string name = (string)args["name"] ?? "shot";
            if (name.Length == 0 || name.Trim('.').Length == 0 || name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException($"photo: name '{name}' must be a bare file name; put the folder in dir");
            string dir = (string)args["dir"];
            if (dir != null && !System.IO.Path.IsPathRooted(dir))
                throw new ArgumentException($"photo: dir '{dir}' must be an absolute path");
            string folder = System.IO.Path.GetFullPath(dir ?? System.IO.Path.Combine(Application.persistentDataPath, "Screenshots"));
            System.IO.Directory.CreateDirectory(folder);
            string path = System.IO.Path.Combine(folder, name + ".png");
            GameManager.instance.StartCoroutine(Photo(path, (bool?)args["ui"] ?? false));
            return new JObject { ["path"] = path };
        }

        // Photo Mode's capture (PhotoModeUISystem.CaptureScreenshot), so the file is what its button would save.
        private static System.Collections.IEnumerator Photo(string path, bool withUI)
        {
            var ui = GameManager.instance.userInterface;
            if (ui != null && !withUI)
                ui.view.enabled = false;
            yield return new WaitForEndOfFrame();
            Texture2D texture = Colossal.PSI.Common.PlatformManager.instance.TakeScreenshotAsTexture();
            System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.Destroy(texture);
            if (ui != null)
                ui.view.enabled = true;
        }

        private void SetCamera(JObject args)
        {
            var camera = m_Camera.gamePlayController;
            if (camera == null)
                throw new InvalidOperationException("no gameplay camera");
            // A hub focus leaves the orbit controller active, which would ignore everything below.
            if (m_Camera.activeCameraController != (IGameCameraController)camera)
            {
                camera.TryMatchPosition(m_Camera.activeCameraController);
                m_Camera.activeCameraController = camera;
            }
            if (args["wx"] != null)
                camera.pivot = (Vector3)WorldPoint(args);
            if (args["zoom"] != null)
                camera.zoom = (float)args["zoom"];
            float2 angle = camera.angle;
            if (args["yaw"] != null)
                angle.x = (float)args["yaw"];
            if (args["tilt"] != null)
                angle.y = (float)args["tilt"];
            camera.angle = angle;
        }

        private JObject CameraState()
        {
            var camera = m_Camera.gamePlayController;
            return new JObject
            {
                ["pivot"] = Vec(camera.pivot),
                ["position"] = Vec(camera.position),
                ["zoom"] = Math.Round(camera.zoom, 1),
                ["yaw"] = Math.Round(camera.angle.x, 1),
                ["tilt"] = Math.Round(camera.angle.y, 1),
            };
        }

        private JObject SetSimulation(JObject args)
        {
            if (args["speed"] != null)
                m_Simulation.selectedSpeed = (float)args["speed"];
            return new JObject { ["speed"] = m_Simulation.selectedSpeed, ["frameIndex"] = m_Simulation.frameIndex };
        }

        private JObject TimeState()
        {
            return new JObject
            {
                ["dateTime"] = m_Time.GetCurrentDateTime().ToString("yyyy-MM-dd HH:mm"),
                ["normalizedTime"] = Math.Round(m_Time.normalizedTime, 4),
                ["normalizedDate"] = Math.Round(m_Time.normalizedDate, 4),
            };
        }

        private JObject AddMoney(JObject args)
        {
            PlayerMoney money = EntityManager.GetComponentData<PlayerMoney>(m_City.City);
            if (args["add"] != null)
                money.Add((int)args["add"]);
            if (args["unlimited"] != null)
                money.m_Unlimited = (bool)args["unlimited"];
            EntityManager.SetComponentData(m_City.City, money);
            return new JObject { ["money"] = money.money, ["unlimited"] = money.m_Unlimited };
        }

        private JObject SetClimate(JObject args)
        {
            Override(m_Climate.currentDate, args["date"]);
            Override(m_Climate.temperature, args["temperature"]);
            Override(m_Climate.precipitation, args["precipitation"]);
            Override(m_Climate.cloudiness, args["cloudiness"]);
            return ClimateState();
        }

        /// <summary>A number overrides the property. null hands it back to the simulation. A missing key leaves it as it is.</summary>
        private static void Override(Game.OverridableProperty<float> property, JToken value)
        {
            if (value == null)
                return;
            if (value.Type == JTokenType.Null)
                property.overrideState = false;
            else
                property.overrideValue = (float)value;
        }

        private JObject ClimateState()
        {
            return new JObject
            {
                ["date"] = Property(m_Climate.currentDate),
                ["temperature"] = Property(m_Climate.temperature),
                ["precipitation"] = Property(m_Climate.precipitation),
                ["cloudiness"] = Property(m_Climate.cloudiness),
            };
        }

        private static JObject Property(Game.OverridableProperty<float> property)
        {
            return new JObject { ["value"] = Math.Round((float)property, 3), ["overridden"] = property.overrideState };
        }

        private static JArray Vec(float3 v)
        {
            return new JArray(Math.Round(v.x, 2), Math.Round(v.y, 2), Math.Round(v.z, 2));
        }

        private static string EntityId(Entity entity)
        {
            return $"{entity.Index}:{entity.Version}";
        }

        private string PrefabNameOf(Entity entity)
        {
            if (entity == Entity.Null || !EntityManager.HasComponent<PrefabRef>(entity))
                return null;
            return m_Prefabs.GetPrefabName(EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab);
        }
    }
}
