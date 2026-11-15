// Op "snow": the game's own snow depth at world points, read from SnowSystem.SnowDepth (the read buffer) the way vanilla's
// SnowSystem.Serialize reads it for a save (AsyncGPUReadback + WaitForCompletion, SnowSystem.cs:244-266, decompile 1.6.4f1).
// Texel (x, y) covers playableOffset + ((x, y) + 0.5) / 1024 * playableArea (snow UV = playable UV, TerrainSystem.cs:2502-2508).
// The red channel is the depth, 0..65535 = 0..8 m (kSnowHeightScale). The green channel (water) is not read.
// Use it to compare a run's state in the hub with the snow the game holds under it (T183). Read-only: nothing is written back.

using System;
using Game.Simulation;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace SkiTestBridge
{
    public partial class TestBridgeSystem
    {
        private const int kSnowTex = 1024;
        private const float kSnowScaleM = 8f;
        private const int kMaxSnowPoints = 4096;

        /// <summary>Op "snow": {points: [[x, z], ...]} and/or a grid {x0, z0, x1, z1, step}. Replies depthM per point (null outside the
        /// playable area), and over all points: count, mean, max, and the shares at or above 0.91 m (the open bar) and 0.30 m (the keep
        /// bar). One full readback per call (4 MB), so batch the points.</summary>
        private JObject SnowCommand(JObject args)
        {
            var points = new System.Collections.Generic.List<float2>();
            if (args["points"] is JArray list)
            {
                foreach (JToken p in list)
                    points.Add(new float2((float)p[0], (float)p[1]));
            }
            if (args["step"] != null)
            {
                float x0 = (float)args["x0"], z0 = (float)args["z0"], x1 = (float)args["x1"], z1 = (float)args["z1"], step = (float)args["step"];
                if (!(step > 0f))
                    throw new ArgumentException("step must be positive");
                for (float z = z0; z <= z1; z += step)
                {
                    for (float x = x0; x <= x1; x += step)
                    {
                        points.Add(new float2(x, z));
                        if (points.Count > kMaxSnowPoints)
                            throw new ArgumentException($"at most {kMaxSnowPoints} points");
                    }
                }
            }
            if (points.Count == 0 || points.Count > kMaxSnowPoints)
                throw new ArgumentException($"give 1 to {kMaxSnowPoints} points (points and/or a grid)");

            var data = ReadSnow(out float2 offset, out float2 area);
            try
            {
                var rows = new JArray();
                SnowAgg agg = default;
                foreach (float2 p in points)
                {
                    double d = SnowAt(data, offset, area, p);
                    rows.Add(new JArray(p.x, p.y, double.IsNaN(d) ? (double?)null : Math.Round(d, 3)));
                    agg.Add(d);
                }
                JObject result = agg.ToJson();
                result["points"] = rows;
                return result;
            }
            finally
            {
                data.Dispose();
            }
        }

        /// <summary>Op "runSnow": {step (m, default 8), limit (default 200), optional entity (an index)}. The hub's run rows next to the
        /// game's snow inside each run's outline. Per run: entity, name, kind, the hub's openState (0 closed, 1 patchy, 2 open, -1 none),
        /// snowPct, estimated, depthCm (-1 = not measured), gunWhy, and `game` = the snow over a step-m grid inside the outline (count,
        /// meanM, maxM, shareOpenBar, shareKeepBar). The stats rows refresh only while the hub panel is open: open it first.</summary>
        private JObject RunSnowCommand(JObject args)
        {
            float step = args["step"] != null ? (float)args["step"] : 8f;
            int limit = args["limit"] != null ? (int)args["limit"] : 200;
            int only = args["entity"] != null ? (int)args["entity"] : -1;
            if (!(step >= 1f))
                throw new ArgumentException("step must be at least 1 m");

            Type statsType = FindTypeByName("SkiResortMod.Systems.SkiResortStatsSystem");
            object stats = statsType != null ? World.GetExistingSystemManaged(statsType) : null;
            if (stats == null)
                throw new InvalidOperationException("SkiResortStatsSystem not found (is Modular Ski Resort loaded?)");
            int runCount = (int)statsType.GetProperty("RunCount").GetValue(stats);
            int liveCount = (int)statsType.GetProperty("RunLiveCount").GetValue(stats);
            System.Reflection.MethodInfo getRun = statsType.GetMethod("GetRun");
            System.Reflection.MethodInfo getLive = statsType.GetMethod("GetRunLive");

            var data = ReadSnow(out float2 offset, out float2 area);
            try
            {
                var rows = new JArray();
                int n = Math.Min(runCount, liveCount);
                for (int i = 0; i < n && rows.Count < limit; i++)
                {
                    object run = getRun.Invoke(stats, new object[] { i });
                    object live = getLive.Invoke(stats, new object[] { i });
                    Entity e = (Entity)Field(run, "Entity");
                    if (only >= 0 && e.Index != only)
                        continue;
                    var row = new JObject
                    {
                        ["entity"] = e.Index,
                        ["name"] = (string)Field(run, "Name"),
                        ["kind"] = (int)Field(run, "Kind"),
                        ["bound"] = (bool)Field(run, "Bound"),
                        ["openState"] = (int)Field(live, "OpenState"),
                        ["snowPct"] = (int)Field(live, "SnowPct"),
                        ["estimated"] = (bool)Field(live, "SnowEstimated"),
                        ["depthCm"] = (int)Field(live, "SnowDepthCm"),
                        ["gunWhy"] = (int)Field(live, "GunWhy"),
                    };
                    if (EntityManager.Exists(e) && EntityManager.HasBuffer<Game.Areas.Node>(e))
                    {
                        DynamicBuffer<Game.Areas.Node> nodes = EntityManager.GetBuffer<Game.Areas.Node>(e, true);
                        var ring = new float2[nodes.Length];
                        float2 lo = new float2(float.MaxValue), hi = new float2(float.MinValue);
                        for (int k = 0; k < nodes.Length; k++)
                        {
                            ring[k] = nodes[k].m_Position.xz;
                            lo = math.min(lo, ring[k]);
                            hi = math.max(hi, ring[k]);
                        }
                        SnowAgg agg = default;
                        int cells = 0;
                        for (float z = lo.y + step * 0.5f; z <= hi.y && cells < kMaxSnowPoints; z += step)
                        {
                            for (float x = lo.x + step * 0.5f; x <= hi.x && cells < kMaxSnowPoints; x += step)
                            {
                                var p = new float2(x, z);
                                if (!InRing(ring, p))
                                    continue;
                                cells++;
                                agg.Add(SnowAt(data, offset, area, p));
                            }
                        }
                        JObject game = agg.ToJson();
                        game["center"] = new JArray(Math.Round((lo.x + hi.x) * 0.5f, 1), Math.Round((lo.y + hi.y) * 0.5f, 1));
                        row["game"] = game;
                    }
                    rows.Add(row);
                }
                return new JObject { ["runs"] = rows, ["runRows"] = runCount, ["liveRows"] = liveCount };
            }
            finally
            {
                data.Dispose();
            }
        }

        private NativeArray<ushort> ReadSnow(out float2 offset, out float2 area)
        {
            SnowSystem snow = World.GetOrCreateSystemManaged<SnowSystem>();
            RenderTexture tex = snow.SnowDepth;
            if (tex == null)
                throw new InvalidOperationException("no snow texture (not in a game?)");
            offset = m_Terrain.playableOffset;
            area = m_Terrain.playableArea;
            if (!(area.x > 0f) || !(area.y > 0f))
                throw new InvalidOperationException("terrain not ready (playableArea is zero)");
            var data = new NativeArray<ushort>(kSnowTex * kSnowTex * 2, Allocator.Persistent);
            AsyncGPUReadbackRequest req = AsyncGPUReadback.RequestIntoNativeArray(ref data, tex);
            req.WaitForCompletion();
            if (!req.done || req.hasError)
            {
                data.Dispose();
                throw new InvalidOperationException("snow readback failed");
            }
            return data;
        }

        /// <summary>Depth in m at p, NaN outside the playable area.</summary>
        private static double SnowAt(NativeArray<ushort> data, float2 offset, float2 area, float2 p)
        {
            float2 uv = (p - offset) / area;
            int tx = (int)math.floor(uv.x * kSnowTex), ty = (int)math.floor(uv.y * kSnowTex);
            if (tx < 0 || ty < 0 || tx >= kSnowTex || ty >= kSnowTex)
                return double.NaN;
            return data[(ty * kSnowTex + tx) * 2] / 65535.0 * kSnowScaleM;
        }

        private static bool InRing(float2[] ring, float2 p)
        {
            bool inside = false;
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            {
                float2 a = ring[i], b = ring[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }

        private static object Field(object o, string name)
        {
            System.Reflection.FieldInfo f = o.GetType().GetField(name);
            if (f == null)
                throw new InvalidOperationException("no field " + name + " on " + o.GetType().Name);
            return f.GetValue(o);
        }

        private static Type FindTypeByName(string fullName)
        {
            foreach (System.Reflection.Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = asm.GetType(fullName, false);
                if (t != null)
                    return t;
            }
            return null;
        }

        private struct SnowAgg
        {
            private int m_N, m_Open, m_Keep;
            private double m_Sum, m_Max;

            public void Add(double d)
            {
                if (double.IsNaN(d))
                    return;
                m_N++;
                m_Sum += d;
                m_Max = Math.Max(m_Max, d);
                if (d >= 0.91)
                    m_Open++;
                if (d >= 0.30)
                    m_Keep++;
            }

            public JObject ToJson() => new JObject
            {
                ["count"] = m_N,
                ["meanM"] = m_N > 0 ? Math.Round(m_Sum / m_N, 3) : (double?)null,
                ["maxM"] = Math.Round(m_Max, 3),
                ["shareOpenBar"] = m_N > 0 ? Math.Round((double)m_Open / m_N, 3) : (double?)null,
                ["shareKeepBar"] = m_N > 0 ? Math.Round((double)m_Keep / m_N, 3) : (double?)null,
            };
        }
    }
}
