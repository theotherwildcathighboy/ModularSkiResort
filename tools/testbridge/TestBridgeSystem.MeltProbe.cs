// Op "meltprobe" (T222, round 2): measures the game's own snow melt on flat patches the probe lays down itself.
//   write : {rects: [{x0, z0, x1, z1, depthM}]}  sets the snow depth (R channel) of every texel whose center lies in a rect, through
//           the vanilla Load kernel the mod's injection uses (snowUpdate "LoadR16G16_UNorm", SnowSystem.Deserialize path). G (water)
//           round-trips. Reply: texels per rect and the sim frame.
//   start : {rects: [{name, x0, z0, x1, z1}], every (sim frames, default 64, at least 4), max (samples, default 200)} samples each
//           rect every `every` frames from OnUpdate (frame-exact, no call latency): mean / min / max depth of its texels, the forced or
//           simulated temperature, precipitation, the time of day. The first reply gives per rect the mean terrain height, the
//           terrain gradient (aspect) and the local air by the vanilla lapse (temperature - snowTemperatureHeightScale x (h - base)).
//   read  : {from} the samples.   stop : ends sampling.
// Texel (x, y) covers playableOffset + ((x, y) + 0.5) / 1024 x playableArea (TerrainSystem.cs:2502-2508), as the "snow" op reads it.
// Dev only: it writes the snow map of the loaded city. Never save the city after a probe.

using System;
using System.Collections.Generic;
using Colossal.IO.AssetDatabase;
using Game.Simulation;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace SkiTestBridge
{
    public partial class TestBridgeSystem
    {
        private readonly List<JObject> m_MeltSamples = new List<JObject>();
        private readonly List<MeltRect> m_MeltRects = new List<MeltRect>();
        private bool m_MeltOn;
        private int m_MeltEvery = 64, m_MeltMax = 200;
        private uint m_MeltLastFrame;
        private ComputeBuffer m_MeltUpload;

        private struct MeltRect
        {
            public string Name;
            public int Tx0, Ty0, Tx1, Ty1; // inclusive texel bounds
        }

        private JObject MeltProbeCommand(JObject args)
        {
            switch ((string)args["action"])
            {
                case "write":
                    return MeltWrite(args);
                case "start":
                {
                    m_MeltRects.Clear();
                    var info = new JArray();
                    foreach (JToken r in (JArray)args["rects"])
                    {
                        MeltRect rect = RectTexels(r);
                        rect.Name = (string)r["name"] ?? ("r" + m_MeltRects.Count);
                        m_MeltRects.Add(rect);
                        info.Add(RectInfo(r, rect));
                    }
                    m_MeltEvery = Math.Max(4, (int?)args["every"] ?? 64);
                    m_MeltMax = Math.Max(1, (int?)args["max"] ?? 200);
                    m_MeltSamples.Clear();
                    m_MeltLastFrame = m_Simulation.frameIndex;
                    m_MeltOn = true;
                    m_MeltSamples.Add(MeltSample());
                    return new JObject
                    {
                        ["started"] = true, ["every"] = m_MeltEvery, ["rects"] = info,
                        ["baseHeight"] = m_Climate.temperatureBaseHeight,
                        ["lapse"] = m_Climate.snowTemperatureHeightScale,
                        ["freezing"] = m_Climate.freezingTemperature,
                    };
                }
                case "read":
                {
                    int from = Math.Max(0, (int?)args["from"] ?? 0);
                    var rows = new JArray();
                    for (int i = from; i < m_MeltSamples.Count; i++)
                        rows.Add(m_MeltSamples[i]);
                    return new JObject { ["on"] = m_MeltOn, ["count"] = m_MeltSamples.Count, ["samples"] = rows };
                }
                case "stop":
                    m_MeltOn = false;
                    return new JObject { ["stopped"] = true, ["count"] = m_MeltSamples.Count };
                default:
                    throw new ArgumentException("action: write, start, read, stop");
            }
        }

        private void TickMeltProbe()
        {
            if (!m_MeltOn)
                return;
            uint frame = m_Simulation.frameIndex;
            if (frame - m_MeltLastFrame < (uint)m_MeltEvery)
                return;
            m_MeltLastFrame = frame;
            try
            {
                m_MeltSamples.Add(MeltSample());
                if (m_MeltSamples.Count >= m_MeltMax)
                    m_MeltOn = false;
            }
            catch (Exception e)
            {
                m_MeltOn = false;
                m_MeltSamples.Add(new JObject { ["error"] = e.Message });
            }
        }

        private JObject MeltSample()
        {
            NativeArray<ushort> data = ReadSnow(out _, out _);
            try
            {
                var rects = new JObject();
                foreach (MeltRect r in m_MeltRects)
                {
                    double sum = 0, min = double.MaxValue, max = 0;
                    int n = 0;
                    for (int y = r.Ty0; y <= r.Ty1; y++)
                    {
                        for (int x = r.Tx0; x <= r.Tx1; x++)
                        {
                            double d = data[(y * kSnowTex + x) * 2] / 65535.0 * kSnowScaleM;
                            sum += d;
                            min = Math.Min(min, d);
                            max = Math.Max(max, d);
                            n++;
                        }
                    }
                    rects[r.Name] = new JArray(Math.Round(sum / Math.Max(1, n), 4), Math.Round(min, 4), Math.Round(max, 4));
                }
                return new JObject
                {
                    ["frame"] = m_Simulation.frameIndex,
                    ["t"] = Math.Round((float)m_Climate.temperature, 2),
                    ["p"] = Math.Round((float)m_Climate.precipitation, 3),
                    ["rain"] = m_Climate.isRaining,
                    ["tod"] = Math.Round(m_Time.normalizedTime, 4),
                    ["d"] = rects,
                };
            }
            finally
            {
                data.Dispose();
            }
        }

        private JObject MeltWrite(JObject args)
        {
            SnowSystem snow = World.GetOrCreateSystemManaged<SnowSystem>();
            RenderTexture tex = snow.SnowDepth;
            ComputeShader shader = AssetDatabase.global.resources.shaders.snowUpdate;
            if (tex == null || shader == null || !shader.HasKernel("LoadR16G16_UNorm"))
                throw new InvalidOperationException("snow texture or Load kernel unavailable");
            if (tex.width != kSnowTex || tex.height != kSnowTex || tex.graphicsFormat != UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16_UNorm)
                throw new InvalidOperationException($"snow texture {tex.width}x{tex.height} {tex.graphicsFormat} is not the verified contract");

            NativeArray<ushort> data = ReadSnow(out _, out _);
            var upload = new NativeArray<float2>(kSnowTex * kSnowTex, Allocator.Temp);
            var counts = new JArray();
            try
            {
                for (int i = 0; i < upload.Length; i++)
                    upload[i] = new float2(data[i * 2] / 65535f, data[i * 2 + 1] / 65535f);
                foreach (JToken r in (JArray)args["rects"])
                {
                    MeltRect rect = RectTexels(r);
                    float norm = math.saturate((float)r["depthM"] / kSnowScaleM);
                    int n = 0;
                    for (int y = rect.Ty0; y <= rect.Ty1; y++)
                    {
                        for (int x = rect.Tx0; x <= rect.Tx1; x++)
                        {
                            int i = y * kSnowTex + x;
                            upload[i] = new float2(norm, upload[i].y);
                            n++;
                        }
                    }
                    counts.Add(n);
                }
                if (m_MeltUpload == null)
                    m_MeltUpload = new ComputeBuffer(kSnowTex * kSnowTex, UnsafeUtility.SizeOf<float2>(), ComputeBufferType.Default);
                m_MeltUpload.SetData(upload);
                int kernel = shader.FindKernel("LoadR16G16_UNorm");
                shader.SetBuffer(kernel, Shader.PropertyToID("_LoadSourceR16G16_UNorm"), m_MeltUpload);
                shader.SetTexture(kernel, Shader.PropertyToID("_Result"), tex);
                shader.Dispatch(kernel, 64, 64, 1);
            }
            finally
            {
                data.Dispose();
                upload.Dispose();
            }
            return new JObject { ["texels"] = counts, ["frame"] = m_Simulation.frameIndex };
        }

        private MeltRect RectTexels(JToken r)
        {
            float2 offset = m_Terrain.playableOffset, area = m_Terrain.playableArea;
            float x0 = (float)r["x0"], z0 = (float)r["z0"], x1 = (float)r["x1"], z1 = (float)r["z1"];
            // Texels whose centers lie inside the rect.
            int tx0 = (int)math.ceil((math.min(x0, x1) - offset.x) / area.x * kSnowTex - 0.5f);
            int tx1 = (int)math.floor((math.max(x0, x1) - offset.x) / area.x * kSnowTex - 0.5f);
            int ty0 = (int)math.ceil((math.min(z0, z1) - offset.y) / area.y * kSnowTex - 0.5f);
            int ty1 = (int)math.floor((math.max(z0, z1) - offset.y) / area.y * kSnowTex - 0.5f);
            tx0 = math.clamp(tx0, 0, kSnowTex - 1);
            ty0 = math.clamp(ty0, 0, kSnowTex - 1);
            tx1 = math.clamp(tx1, 0, kSnowTex - 1);
            ty1 = math.clamp(ty1, 0, kSnowTex - 1);
            if (tx1 < tx0 || ty1 < ty0)
                throw new ArgumentException("rect holds no texel center (a texel is about 14 m)");
            return new MeltRect { Tx0 = tx0, Ty0 = ty0, Tx1 = tx1, Ty1 = ty1 };
        }

        private JObject RectInfo(JToken r, MeltRect rect)
        {
            float x0 = (float)r["x0"], z0 = (float)r["z0"], x1 = (float)r["x1"], z1 = (float)r["z1"];
            float cx = (x0 + x1) * 0.5f, cz = (z0 + z1) * 0.5f;
            float hx = math.abs(x1 - x0) * 0.5f, hz = math.abs(z1 - z0) * 0.5f;
            double sum = 0;
            int n = 0;
            for (int i = 0; i <= 4; i++)
            {
                for (int j = 0; j <= 4; j++)
                {
                    sum += TerrainHeight(cx - hx + hx * 0.5f * i, cz - hz + hz * 0.5f * j);
                    n++;
                }
            }
            float h = (float)(sum / n);
            // Gradient: dh/dx, dh/dz (m per m). Downhill points along -gradient.
            float gx = (TerrainHeight(cx + hx, cz) - TerrainHeight(cx - hx, cz)) / math.max(1f, 2f * hx);
            float gz = (TerrainHeight(cx, cz + hz) - TerrainHeight(cx, cz - hz)) / math.max(1f, 2f * hz);
            float air = (float)m_Climate.temperature - m_Climate.snowTemperatureHeightScale * (h - m_Climate.temperatureBaseHeight);
            return new JObject
            {
                ["name"] = (string)r["name"],
                ["texels"] = (rect.Tx1 - rect.Tx0 + 1) * (rect.Ty1 - rect.Ty0 + 1),
                ["heightM"] = Math.Round(h, 1),
                ["gradX"] = Math.Round(gx, 3),
                ["gradZ"] = Math.Round(gz, 3),
                ["airC"] = Math.Round(air, 2),
            };
        }
    }
}
