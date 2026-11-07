// Op "pm": Photo Mode through the game's own properties (PhotoModeRenderSystem.photoModeProperties, the sliders of the panel).
// A shot's settings then mean what they mean in the panel. README.md, "Photo Mode".

using System;
using Game.Rendering;
using Game.Rendering.CinematicCamera;
using Game.UI.InGame;
using Newtonsoft.Json.Linq;
using Unity.Entities;
using UnityEngine;

namespace SkiTestBridge
{
    public partial class TestBridgeSystem
    {
        private JObject PhotoMode(JObject args)
        {
            var ui = World.GetOrCreateSystemManaged<PhotoModeUISystem>();
            var render = World.GetOrCreateSystemManaged<PhotoModeRenderSystem>();
            if (args["on"] != null)
                ui.Activate((bool)args["on"]);
            if (args["cam"] is JObject cam)
                SetCinematicCamera(cam);
            if (args["set"] is JObject set)
            {
                foreach (var row in set.Properties())
                    SetPhotoProperty(render, row.Name, row.Value);
            }
            if ((bool?)args["reset"] ?? false)
            {
                foreach (var pair in render.photoModeProperties)
                    pair.Value.reset?.Invoke();
                render.DisableAllCameraProperties();
            }
            var result = new JObject
            {
                ["cinematic"] = m_Camera.activeCameraController == (IGameCameraController)m_Camera.cinematicCameraController,
            };
            if (m_Camera.cinematicCameraController != null)
            {
                result["position"] = new JArray(m_Camera.cinematicCameraController.position.x, m_Camera.cinematicCameraController.position.y, m_Camera.cinematicCameraController.position.z);
                result["rotation"] = new JArray(m_Camera.cinematicCameraController.rotation.x, m_Camera.cinematicCameraController.rotation.y, m_Camera.cinematicCameraController.rotation.z);
            }
            if ((bool?)args["list"] ?? false)
                result["properties"] = PhotoProperties(render, (string)args["group"]);
            return result;
        }

        /// <summary>x, y, z, then yaw and pitch in degrees, or at: [x, y, z] to look at a world point. Roll stays the "Roll" property.</summary>
        private void SetCinematicCamera(JObject cam)
        {
            var camera = m_Camera.cinematicCameraController;
            if (camera == null || m_Camera.activeCameraController != (IGameCameraController)camera)
                throw new InvalidOperationException("Photo Mode is off: {op:\"pm\",on:true} first");
            Vector3 position = camera.position;
            if (cam["x"] != null)
                position = new Vector3((float)cam["x"], (float)cam["y"], (float)cam["z"]);
            camera.position = position;
            Vector3 euler = camera.rotation;
            if (cam["at"] is JArray at)
            {
                Vector3 look = new Vector3((float)at[0], (float)at[1], (float)at[2]) - position;
                Quaternion q = Quaternion.LookRotation(look.normalized, Vector3.up);
                euler = new Vector3(q.eulerAngles.x, q.eulerAngles.y, 0f);
            }
            if (cam["yaw"] != null)
                euler.y = (float)cam["yaw"];
            if (cam["pitch"] != null)
                euler.x = (float)cam["pitch"];
            camera.rotation = euler;
            camera.SnapToTarget();
        }

        /// <summary>A number sets the property (and its override). null turns the override off, like the panel's checkbox.</summary>
        private static void SetPhotoProperty(PhotoModeRenderSystem render, string id, JToken value)
        {
            foreach (var pair in render.photoModeProperties)
            {
                if (pair.Key != id)
                    continue;
                if (value.Type == JTokenType.Null)
                {
                    pair.Value.setEnabled?.Invoke(false);
                    return;
                }
                pair.Value.setEnabled?.Invoke(true);
                pair.Value.setValue?.Invoke((float)value);
                return;
            }
            throw new ArgumentException($"no Photo Mode property '{id}'");
        }

        private static JArray PhotoProperties(PhotoModeRenderSystem render, string group)
        {
            var rows = new JArray();
            foreach (var pair in render.photoModeProperties)
            {
                PhotoModeProperty p = pair.Value;
                if (p.getValue == null || (group != null && p.group != group))
                    continue;
                var row = new JObject { ["id"] = p.id, ["group"] = p.group, ["value"] = Math.Round(p.getValue(), 4) };
                if (p.min != null)
                    row["min"] = Math.Round(p.min(), 4);
                if (p.max != null)
                    row["max"] = Math.Round(p.max(), 4);
                if (p.isEnabled != null)
                    row["enabled"] = p.isEnabled();
                if (p.isAvailable != null)
                    row["available"] = p.isAvailable();
                rows.Add(row);
            }
            return rows;
        }
    }
}
