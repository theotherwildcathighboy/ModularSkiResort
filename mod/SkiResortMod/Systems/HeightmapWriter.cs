// THE one place the mod writes the game's base terrain heightmap. Used by CatTrackGradeToolSystem (apply / undo) and its
// dev-tab probe. Not a system: a helper the tool owns, main thread only.
//
// WHY THE BASE MAP (1.6.0f1): TerrainSystem keeps two height layers. The base heightmap m_Heightmap (R16_UNorm RenderTexture,
// SetHeightmap TerrainSystem.cs:3231-3250) is the only layer the save stores (SerializeHeightmap :3030-3056). The cascade the
// game renders and reads back to the CPU is re-blitted from it with the net / lot / area overlays drawn on top (RenderCascade
// :4833-4925), so a cascade edit never lasts. The vanilla terrain brush writes the base map: ApplyBrush (:3942-3954) ->
// ApplyToTerrain (:3715-3935, compute shader into m_Heightmap) -> UpdateMinMax -> TriggerAsyncChange.
//
// THE WRITE (vanilla's own public calls, no reflection):
//   1. Read : AsyncGPUReadback.Request(heightmap, mip 0, x, w, y, h, 0, 1).WaitForCompletion(), the sub-rect form of the
//              save's readback (:3043).
//   2. Write: a temporary Texture2D R16_UNorm of the rect, SetPixelData + Apply, then Graphics.CopyTexture(src, 0, 0, 0, 0,
//              w, h, heightmap, 0, 0, x, y): the sub-rect form of the copy SetHeightmap makes (:3250). Same format both sides
//              (the D3D11 CopyTexture rule). Whether the sub-rect copy lands was unverified, so every write is verified by
//              reading the rect back and comparing every texel (a mismatch is an ERROR and the caller restores the pre-image).
//   3. Tail : WaterSystem.TerrainWillChangeFromBrush(area) (WaterSystem.cs:873-889: pauses the water sim for 15 frames),
//              TerrainSystem.OnAreaChanged(area) (:5096-5108: the cascade re-renders the area, GroundHeightSystem re-snaps
//              trees / props), UpdateMinMax(default(Brush), area) (:3956), TriggerAsyncChange() (:3622: the CPU height
//              readback refreshes). ApplyToTerrain's m_TerrainChange / m_TerrainChangeArea are private and only read by
//              GetTerrainBrushUpdate (:2642), which nothing in Game.dll calls: not needed.
//   FORMAT GATE (once per load, logged): the heightmap must be a created RenderTexture, R16_UNorm, at least 1024 texels a
//   side, with a positive playable area and height scale; anything else refuses every write (texture formats have changed
//   without patch notes before). The mapping uses playableArea / playableOffset / heightScaleOffset, never the 14,336 m
//   defaults (custom maps).
//
// LOGS: every read and write is one line (rect, texels, ms, verify result); the gate one line per load; failures WARN / ERROR.

using System;
using System.Globalization;
using Colossal.Mathematics;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace SkiResortMod.Systems
{
    internal sealed class HeightmapWriter
    {
        private readonly TerrainSystem m_Terrain;
        private readonly WaterSystem m_Water;
        private string m_GateLogged;

        public HeightmapWriter(TerrainSystem terrain, WaterSystem water)
        {
            m_Terrain = terrain;
            m_Water = water;
        }

        public string LastLine { get; private set; } = "-";

        /// <summary>The live base-map mapping, or false with the reason (header FORMAT GATE). Logs the gate once per distinct result.</summary>
        public bool TryMap(out GradeMap map, out string why)
        {
            map = default;
            why = null;
            Texture tex = m_Terrain.heightmap;
            var rt = tex as RenderTexture;
            if (tex == null)
                why = "no heightmap (no map loaded)";
            else if (rt == null || !rt.IsCreated())
                why = $"heightmap is a {tex.GetType().Name} that is not a created RenderTexture";
            else if (rt.graphicsFormat != GraphicsFormat.R16_UNorm)
                why = $"heightmap format {rt.graphicsFormat}, expected R16_UNorm (the game changed its terrain format)";
            else if (rt.width < 1024 || rt.height < 1024)
                why = $"heightmap {rt.width} x {rt.height}, expected at least 1024 a side";
            else
            {
                map = new GradeMap
                {
                    Offset = m_Terrain.playableOffset,
                    Area = m_Terrain.playableArea,
                    Res = new int2(rt.width, rt.height),
                    HeightScale = m_Terrain.heightScaleOffset.x,
                    HeightOffset = m_Terrain.heightScaleOffset.y,
                };
                if (!map.Valid)
                    why = $"playable area {map.Area} / height scale {map.HeightScale} not positive";
            }
            string gate = why ?? string.Format(CultureInfo.InvariantCulture,
                "OK — {0} x {1} R16_UNorm, playable area {2:F0} x {3:F0} m at ({4:F0}, {5:F0}) = {6:F3} m texels, heights {7:F0} + {8:F0} m " +
                "(step {9:F4} m), CopyTexture support {10}", map.Res.x, map.Res.y, map.Area.x, map.Area.y, map.Offset.x, map.Offset.y,
                map.Cell.x, map.HeightOffset, map.HeightScale, map.StepM, SystemInfo.copyTextureSupport);
            if (!string.Equals(gate, m_GateLogged, StringComparison.Ordinal))
            {
                m_GateLogged = gate;
                if (why == null)
                    Mod.Log.Info($"Grade [heightmap]: format gate {gate}");
                else
                    Mod.Log.Warn($"Grade [heightmap]: format gate REFUSED — {why}; grading is off for this map");
            }
            return why == null;
        }

        /// <summary>Forget the logged gate (a load: the next TryMap logs again).</summary>
        public void ResetGate() => m_GateLogged = null;

        /// <summary>World bounds of a texel rect (x, z, w, h).</summary>
        public static Bounds2 WorldBounds(GradeMap map, int4 rect)
        {
            float2 min = map.Offset + new float2(rect.x, rect.y) * map.Cell;
            float2 max = map.Offset + new float2(rect.x + rect.z, rect.y + rect.w) * map.Cell;
            return new Bounds2(min, max);
        }

        /// <summary>Synchronous readback of a base-map rect (header THE WRITE 1); null on a readback error (logged).</summary>
        public ushort[] Read(int4 rect, string why)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Texture tex = m_Terrain.heightmap;
            AsyncGPUReadbackRequest req = AsyncGPUReadback.Request(tex, 0, rect.x, rect.z, rect.y, rect.w, 0, 1);
            req.WaitForCompletion();
            if (req.hasError)
            {
                Mod.Log.Warn($"Grade [heightmap]: READ FAILED ({why}) — readback error for rect {Rect(rect)}");
                return null;
            }
            NativeArray<ushort> data = req.GetData<ushort>();
            if (data.Length != rect.z * rect.w)
            {
                Mod.Log.Warn($"Grade [heightmap]: READ FAILED ({why}) — {data.Length} values for rect {Rect(rect)} ({rect.z * rect.w} expected)");
                return null;
            }
            ushort[] result = data.ToArray();
            sw.Stop();
            Mod.Log.Info(string.Format(CultureInfo.InvariantCulture, "Grade [heightmap]: read {0} ({1}): {2} texels in {3:F1} ms",
                Rect(rect), why, result.Length, sw.Elapsed.TotalMilliseconds));
            return result;
        }

        /// <summary>Write a rect into the base map, verify it by readback and run the vanilla notification tail (header THE WRITE 2-3).
        /// <paramref name="mismatches"/> = texels the readback did not find as written (0 = the write landed); -1 when the verify
        /// readback itself failed. Returns false when nothing was attempted (gate) or the verify found mismatches.</summary>
        public bool Write(GradeMap map, int4 rect, ushort[] data, string why, out int mismatches)
        {
            mismatches = -1;
            if (data == null || data.Length != rect.z * rect.w)
            {
                Mod.Log.Warn($"Grade [heightmap]: WRITE REFUSED ({why}) — {data?.Length ?? 0} values for rect {Rect(rect)}");
                return false;
            }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Texture target = m_Terrain.heightmap;
            var src = new Texture2D(rect.z, rect.w, GraphicsFormat.R16_UNorm, TextureCreationFlags.None)
            {
                name = "SkiGradeRect",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            double copyMs;
            try
            {
                src.SetPixelData(data, 0);
                src.Apply(false, false);
                Graphics.CopyTexture(src, 0, 0, 0, 0, rect.z, rect.w, target, 0, 0, rect.x, rect.y);
                copyMs = sw.Elapsed.TotalMilliseconds;
            }
            finally
            {
                UnityEngine.Object.Destroy(src);
            }

            // Verify by readback (forces the GPU to finish the copy).
            ushort[] back = Read(rect, why + " verify");
            if (back != null)
            {
                mismatches = 0;
                for (int t = 0; t < back.Length; t++)
                    if (back[t] != data[t])
                        mismatches++;
            }

            // The vanilla tail, also on a mismatch: whatever did land must reach the cascade, the CPU heights and the water.
            Bounds2 area = WorldBounds(map, rect);
            m_Water.TerrainWillChangeFromBrush(area);
            m_Terrain.OnAreaChanged(in area);
            m_Terrain.UpdateMinMax(default(Brush), area);
            m_Terrain.TriggerAsyncChange();
            sw.Stop();

            LastLine = string.Format(CultureInfo.InvariantCulture,
                "write {0} ({1}): {2} texels, copy {3:F1} ms, total {4:F1} ms, verify {5}", Rect(rect), why, data.Length, copyMs,
                sw.Elapsed.TotalMilliseconds, mismatches < 0 ? "READBACK FAILED" : mismatches == 0 ? "OK (bit-exact)" : mismatches + " MISMATCH(ES)");
            if (mismatches == 0)
                Mod.Log.Info("Grade [heightmap]: " + LastLine + string.Format(CultureInfo.InvariantCulture,
                    "; tail: TerrainWillChangeFromBrush, OnAreaChanged, UpdateMinMax, TriggerAsyncChange over ({0:F0}, {1:F0}) - ({2:F0}, {3:F0})",
                    area.min.x, area.min.y, area.max.x, area.max.y));
            else
                Mod.Log.Error("Grade [heightmap]: !!! " + LastLine + " — the sub-rect Graphics.CopyTexture into the base heightmap did not " +
                              "land as written (please send this log)");
            return mismatches == 0;
        }

        /// <summary>The CPU (cascade) height at a world point, the heights every game system reads (GetHeightData, :2603).</summary>
        public float CpuHeight(float2 xz)
        {
            TerrainHeightData hd = m_Terrain.GetHeightData();
            return hd.isCreated ? TerrainUtils.SampleHeight(ref hd, new float3(xz.x, 0f, xz.y)) : float.NaN;
        }

        public int WaterSimSpeed => m_Water.WaterSimSpeed;

        public static string Rect(int4 r) => $"({r.x}, {r.y}) {r.z} x {r.w}";
    }
}
