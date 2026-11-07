// The tests/SlopeBatch field captures (format: tests/SlopeBatch/captures/README.txt) as planner input. The polygon becomes the
// region. Its Need heights become the snapshot (texel = lattice node at multiple 1). Every texel that the capture lacks takes the
// height of the nearest captured texel (multi-source BFS). So only ground outside the drawn run is invented.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace AutoGenBatch
{
    internal sealed class Capture
    {
        public string Name;
        public float Step, Texel;
        public int Multiple;
        public double2 Origin;
        public string RatedBand;
        public float2[] Ring;
        public readonly Dictionary<long, float> Heights = new Dictionary<long, float>();

        public static long Key(long k, long l) => (k << 32) ^ (l & 0xFFFFFFFFL);

        public static Capture Load(string path)
        {
            var c = new Capture { Name = Path.GetFileName(path) };
            string[] lines = File.ReadAllLines(path);
            var ring = new List<float2>();
            int gi0 = 0, gj0 = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    continue;
                string[] t = line.Split(' ');
                switch (t[0])
                {
                    case "step":
                        c.Step = F(t[1]);
                        c.Texel = F(t[3]);
                        c.Multiple = int.Parse(t[5], CultureInfo.InvariantCulture);
                        break;
                    case "origin":
                        c.Origin = new double2(double.Parse(t[1], CultureInfo.InvariantCulture), double.Parse(t[2], CultureInfo.InvariantCulture));
                        break;
                    case "grid":
                        gi0 = int.Parse(t[1], CultureInfo.InvariantCulture);
                        gj0 = int.Parse(t[2], CultureInfo.InvariantCulture);
                        break;
                    case "rated":
                        c.RatedBand = t[5];
                        break;
                    case "nodes":
                    {
                        int n = int.Parse(t[1], CultureInfo.InvariantCulture);
                        for (int k = 0; k < n; k++)
                        {
                            string[] p = lines[++i].Trim().Split(' ');
                            ring.Add(new float2(F(p[0]), F(p[2])));
                        }
                        break;
                    }
                    case "need":
                    {
                        int n = int.Parse(t[1], CultureInfo.InvariantCulture);
                        for (int k = 0; k < n; k++)
                        {
                            string[] p = lines[++i].Trim().Split(' ');
                            long li = gi0 + int.Parse(p[0], CultureInfo.InvariantCulture), lj = gj0 + int.Parse(p[1], CultureInfo.InvariantCulture);
                            c.Heights[Key(li, lj)] = F(p[2]);
                        }
                        break;
                    }
                }
            }
            c.Ring = ring.ToArray();
            return c;
        }

        private static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        /// <summary>Snapshot on the vanilla map (the capture's origin must be -Terrain.Offset and its multiple 1). The result is null otherwise.</summary>
        public HeightSnapshot Snapshot()
        {
            if (Multiple != 1 || Origin.x != -Terrain.Offset.x || Origin.y != -Terrain.Offset.z)
                return null;
            SkiRunAutoPlanner.SnapshotRect(Ring, Terrain.Res, Terrain.Res, Terrain.Scale, Terrain.Offset, out int x0, out int z0, out int w, out int d);
            HeightSnapshot s = HeightSnapshot.Allocate(x0, z0, w, d, Terrain.Res, Terrain.Res, Terrain.Scale, Terrain.Offset, false);
            var known = new bool[w * d];
            var queue = new Queue<int>();
            for (int l = 0; l < d; l++)
                for (int k = 0; k < w; k++)
                    if (Heights.TryGetValue(Key(x0 + k, z0 + l), out float h))
                    {
                        s.Heights[l * w + k] = (ushort)Math.Round(h * Terrain.Scale.y);
                        known[l * w + k] = true;
                        queue.Enqueue(l * w + k);
                    }
            while (queue.Count > 0)
            {
                int q = queue.Dequeue();
                int k = q % w, l = q / w;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nk = k + dx, nl = l + dz;
                        if (nk < 0 || nl < 0 || nk >= w || nl >= d || known[nl * w + nk])
                            continue;
                        known[nl * w + nk] = true;
                        s.Heights[nl * w + nk] = s.Heights[q];
                        queue.Enqueue(nl * w + nk);
                    }
            }
            return s;
        }
    }
}
