// Op "runTexels" (round 2, the speed-8 run ramp finding): the injection's own planes under one run's outline, next to the game's snow.
// Reads SkiSnowInjectionSystem's private planes by reflection (m_Mask target depth /8 m, m_WarmMask, m_RampMask chill or the stopped
// mark), the texels whose centers lie inside the outline. Each texel falls in one class:
//   full    a target at or above the open bar (the guns raise it, the run's share and mean count it)
//   fringe  a target under the bar (the feathered edge, at most 0.02 m; the share and mean skip it)
//   warm    too warm for the gate (the game melts it; counted at its depth)
//   stopped a stopped gun run's cold texel (no target this pass; counted)
//   none    inside the outline but no target: dropped by the edge noise (never raised; counted only when no full, warm or stopped
//           texel is there, RunSnowRules.CountDroppedTexels)
// Per class: count, mean depth, the share at the open bar, the mean chill (full only), and depth quantiles of the full texels.
// "countedShare" is the run's open share as the fold forms it from these classes (texel centers only, so an estimate: the fold weighs
// partly covered edge texels by their coverage). Read-only.

using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace SkiTestBridge
{
    public partial class TestBridgeSystem
    {
        private const double kOpenBarM = 0.91;
        private const double kFringeBarNorm = 0.91 / 8.0;
        private const double kHalfQuantum = 0.5 / 65535.0;

        /// <summary>Op "runTexels": {kinds (default [1, 2]: snowmaking, night), optional names [..], optional entity}. Per run the outline's
        /// elevation range (yMin, yMax) and center, and the texel classes of the injection's planes under it (see the file header).</summary>
        private JObject RunTexelsCommand(JObject args)
        {
            var kinds = new HashSet<int>();
            if (args["kinds"] is JArray kl)
                foreach (JToken k in kl)
                    kinds.Add((int)k);
            else
            {
                kinds.Add(1);
                kinds.Add(2);
            }
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (args["names"] is JArray nl)
                foreach (JToken n in nl)
                    names.Add((string)n);
            int only = args["entity"] != null ? (int)args["entity"] : -1;

            Type statsType = FindTypeByName("SkiResortMod.Systems.SkiResortStatsSystem");
            Type injType = FindTypeByName("SkiResortMod.Systems.SkiSnowInjectionSystem");
            object stats = statsType != null ? World.GetExistingSystemManaged(statsType) : null;
            object inj = injType != null ? World.GetExistingSystemManaged(injType) : null;
            if (stats == null || inj == null)
                throw new InvalidOperationException("SkiResortStatsSystem / SkiSnowInjectionSystem not found (is Modular Ski Resort loaded?)");
            NativeArray<float> mask = PlaneOf(inj, injType, "m_Mask");
            NativeArray<float> warm = PlaneOf(inj, injType, "m_WarmMask");
            NativeArray<float> ramp = PlaneOf(inj, injType, "m_RampMask");
            System.Reflection.FieldInfo upField = injType.GetField("m_Upload", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var upload = upField != null ? (NativeArray<float2>)upField.GetValue(inj) : default;
            if (!upload.IsCreated || upload.Length != kSnowTex * kSnowTex)
                throw new InvalidOperationException("m_Upload is not built yet (no write pass?)");

            int runCount = (int)statsType.GetProperty("RunCount").GetValue(stats);
            System.Reflection.MethodInfo getRun = statsType.GetMethod("GetRun");
            var data = ReadSnow(out float2 offset, out float2 area);
            try
            {
                var rows = new JArray();
                for (int i = 0; i < runCount; i++)
                {
                    object run = getRun.Invoke(stats, new object[] { i });
                    Entity e = (Entity)Field(run, "Entity");
                    int kind = (int)Field(run, "Kind");
                    string name = (string)Field(run, "Name");
                    if (only >= 0 ? e.Index != only : (!kinds.Contains(kind) || (names.Count > 0 && !names.Contains(name ?? ""))))
                        continue;
                    if (!EntityManager.Exists(e) || !EntityManager.HasBuffer<Game.Areas.Node>(e))
                        continue;
                    DynamicBuffer<Game.Areas.Node> nodes = EntityManager.GetBuffer<Game.Areas.Node>(e, true);
                    var ring = new float2[nodes.Length];
                    float2 lo = new float2(float.MaxValue), hi = new float2(float.MinValue);
                    float yLo = float.MaxValue, yHi = float.MinValue;
                    for (int k = 0; k < nodes.Length; k++)
                    {
                        ring[k] = nodes[k].m_Position.xz;
                        yLo = math.min(yLo, nodes[k].m_Position.y);
                        yHi = math.max(yHi, nodes[k].m_Position.y);
                        lo = math.min(lo, ring[k]);
                        hi = math.max(hi, ring[k]);
                    }
                    float2 texel = area / kSnowTex;
                    int x0 = math.max(0, (int)math.floor((lo.x - offset.x) / texel.x));
                    int x1 = math.min(kSnowTex - 1, (int)math.floor((hi.x - offset.x) / texel.x));
                    int y0 = math.max(0, (int)math.floor((lo.y - offset.y) / texel.y));
                    int y1 = math.min(kSnowTex - 1, (int)math.floor((hi.y - offset.y) / texel.y));
                    var cls = new Dictionary<string, TexelClass>();
                    foreach (string c in new[] { "full", "fringe", "warm", "stopped", "none" })
                        cls[c] = new TexelClass();
                    var fullDepths = new List<double>();
                    // the full texels by terrain slope (rise over run across one texel): count, mean depth now, mean depth the last pass
                    // uploaded. A map under the upload means the game took snow away between the write and this read.
                    var bins = new SlopeBin[kSlopeEdges.Length + 1];
                    for (int b = 0; b < bins.Length; b++)
                        bins[b] = new SlopeBin();
                    for (int ty = y0; ty <= y1; ty++)
                    {
                        for (int tx = x0; tx <= x1; tx++)
                        {
                            var c = new float2(offset.x + (tx + 0.5f) * texel.x, offset.y + (ty + 0.5f) * texel.y);
                            if (!InRing(ring, c))
                                continue;
                            int idx = ty * kSnowTex + tx;
                            double d = data[idx * 2] / 65535.0 * kSnowScaleM;
                            float m = mask[idx];
                            string k;
                            if (m > 0f)
                                k = m >= 2.0 * kHalfQuantum && m < kFringeBarNorm - kHalfQuantum ? "fringe" : "full";
                            else if (warm[idx] > 0f)
                                k = "warm";
                            else if (ramp[idx] < 0f)
                                k = "stopped";
                            else
                                k = "none";
                            TexelClass t = cls[k];
                            t.N++;
                            t.Sum += d;
                            if (d >= kOpenBarM)
                                t.Open++;
                            if (k == "full")
                            {
                                t.Chill += ramp[idx];
                                fullDepths.Add(d);
                                float hx = 0.5f * texel.x, hz = 0.5f * texel.y;
                                float gx = (TerrainHeight(c.x + hx, c.y) - TerrainHeight(c.x - hx, c.y)) / texel.x;
                                float gz = (TerrainHeight(c.x, c.y + hz) - TerrainHeight(c.x, c.y - hz)) / texel.y;
                                float slope = math.sqrt(gx * gx + gz * gz);
                                int b = 0;
                                while (b < kSlopeEdges.Length && slope >= kSlopeEdges[b])
                                    b++;
                                SlopeBin sb = bins[b];
                                sb.N++;
                                sb.Depth += d;
                                sb.Up += upload[idx].x * kSnowScaleM;
                            }
                        }
                    }
                    var classes = new JObject();
                    int counted = 0, countedOpen = 0;
                    // the fold's rule since round 2: fringe never counts, dropped ("none") only when nothing else is measured
                    bool countNone = cls["full"].N + cls["warm"].N + cls["stopped"].N == 0;
                    foreach (KeyValuePair<string, TexelClass> kv in cls)
                    {
                        TexelClass t = kv.Value;
                        var o = new JObject
                        {
                            ["n"] = t.N,
                            ["meanM"] = t.N > 0 ? Math.Round(t.Sum / t.N, 3) : (double?)null,
                            ["openShare"] = t.N > 0 ? Math.Round((double)t.Open / t.N, 3) : (double?)null,
                        };
                        if (kv.Key == "full")
                            o["meanChill"] = t.N > 0 ? Math.Round(t.Chill / t.N, 3) : (double?)null;
                        classes[kv.Key] = o;
                        if (kv.Key != "fringe" && (kv.Key != "none" || countNone))
                        {
                            counted += t.N;
                            countedOpen += t.Open;
                        }
                    }
                    var bySlope = new JArray();
                    for (int b = 0; b < bins.Length; b++)
                    {
                        if (bins[b].N == 0)
                            continue;
                        bySlope.Add(new JObject
                        {
                            ["slopeFrom"] = b == 0 ? 0f : kSlopeEdges[b - 1],
                            ["n"] = bins[b].N,
                            ["meanM"] = Math.Round(bins[b].Depth / bins[b].N, 3),
                            ["lastUploadM"] = Math.Round(bins[b].Up / bins[b].N, 3),
                        });
                    }
                    fullDepths.Sort();
                    var q = new JObject();
                    foreach (double p in new[] { 0.1, 0.4, 0.5, 0.9 })
                        q["p" + (int)(p * 100)] = fullDepths.Count > 0 ? Math.Round(fullDepths[(int)Math.Min(fullDepths.Count - 1, p * fullDepths.Count)], 3) : (double?)null;
                    rows.Add(new JObject
                    {
                        ["entity"] = e.Index,
                        ["name"] = name,
                        ["kind"] = kind,
                        ["yMin"] = Math.Round(yLo, 1),
                        ["yMax"] = Math.Round(yHi, 1),
                        ["center"] = new JArray(Math.Round((lo.x + hi.x) * 0.5f, 1), Math.Round((lo.y + hi.y) * 0.5f, 1)),
                        ["texels"] = cls["full"].N + cls["fringe"].N + cls["warm"].N + cls["stopped"].N + cls["none"].N,
                        ["classes"] = classes,
                        ["fullQuantilesM"] = q,
                        ["fullBySlope"] = bySlope,
                        ["countedShare"] = counted > 0 ? Math.Round((double)countedOpen / counted, 3) : (double?)null,
                        ["countedTexels"] = counted,
                    });
                }
                return new JObject { ["runs"] = rows, ["frame"] = (long)World.GetExistingSystemManaged<Game.Simulation.SimulationSystem>().frameIndex };
            }
            finally
            {
                data.Dispose();
            }
        }

        private static NativeArray<float> PlaneOf(object inj, Type injType, string field)
        {
            System.Reflection.FieldInfo f = injType.GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (f == null)
                throw new InvalidOperationException("no field " + field + " on the injection");
            var a = (NativeArray<float>)f.GetValue(inj);
            if (!a.IsCreated || a.Length != kSnowTex * kSnowTex)
                throw new InvalidOperationException(field + " is not built yet (no evaluated pass?)");
            return a;
        }

        private static readonly float[] kSlopeEdges = { 0.2f, 0.4f, 0.6f, 0.8f, 1.0f };

        private sealed class SlopeBin
        {
            public int N;
            public double Depth, Up;
        }

        private sealed class TexelClass
        {
            public int N, Open;
            public double Sum, Chill;
        }
    }
}
