// RunsFirstPlanner inputs from the game (port round R2-a, plan section 7.5): ground the plan must not use. Each blocked shape takes its
// cells out of the grid mask (Grid.In), the hole mechanism the "hole" regions of the test host use. Every pass already reads the mask,
// so a blocked cell is never a line, a station, a traverse or a run cell. The pick's area (lines and lifts per km2) is the open area.
// Additive: with no input (Inputs null or empty) the planner does exactly what it did before, and every plan hash stays the same. The
// hooks are two lines in RunsFirstPlanner.cs (ApplyInputs at the end of BuildGrid, BlockedKm2 off the region's km2).
// What the tool passes (QuickRunsScanJob.InputsFrom): other areas and unowned tiles with the realizer's clearances, existing runs with
// the density's gap, the wet texels of the snapshot, and each existing lift as its stations and a corridor along its line. Existing
// cat tracks are passable (no shape). Access points are the bottom landing class 2 of the way to the bottom (plan 5.1, Bottom.cs).
// Pure C# 9 on System and Unity.Mathematics, net48-clean. See EngineNotes/RunsFirstPlanner.md (Inputs).

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>Ground the planner must leave alone, and facts about the site it may read. Empty = no input.</summary>
    public sealed class RunsFirstInputs
    {
        /// <summary>A blocked shape: a ring of 3 or more points (inside, and within ClearM of its edges), a segment of 2 points, or a
        /// point (within ClearM). A cell is blocked when its center is blocked.</summary>
        public sealed class Shape
        {
            public float2[] Pts;
            public float ClearM;
        }

        public readonly List<Shape> Blocked = new List<Shape>();
        /// <summary>The wet texels of the snapshot (HeightSnapshot.Wet) block their cells: a cell is wet when any of its texels is.</summary>
        public bool Water;
        /// <summary>Road samples and the Welcome Sign near the region, sorted (AutoPlanInput.Access). The way-to-the-bottom pass reads them: flat
        /// base-band ground in reach of one is its landing class 2 (plan 5.1, Bottom.cs).</summary>
        public float2[] Access;

        /// <summary>True when the inputs change nothing in the plan.</summary>
        public bool Empty => Blocked.Count == 0 && !Water;

        /// <summary>FNV-1a 64 over everything a pass may read (the shapes, the water flag, the access points).</summary>
        public ulong Fingerprint()
        {
            ulong h = 14695981039346656037UL;
            void U(uint v)
            {
                for (int b = 0; b < 4; b++)
                {
                    h ^= (v >> (8 * b)) & 0xFFu;
                    h *= 1099511628211UL;
                }
            }
            U((uint)Blocked.Count);
            foreach (Shape s in Blocked)
            {
                U(math.asuint(s.ClearM));
                U((uint)(s.Pts?.Length ?? 0));
                if (s.Pts != null)
                    foreach (float2 p in s.Pts)
                    {
                        U(math.asuint(p.x));
                        U(math.asuint(p.y));
                    }
            }
            U(Water ? 1u : 0u);
            U((uint)(Access?.Length ?? 0));
            if (Access != null)
                foreach (float2 p in Access)
                {
                    U(math.asuint(p.x));
                    U(math.asuint(p.y));
                }
            return h;
        }
    }

    public sealed partial class RunsFirstPlanner
    {
        /// <summary>The game's inputs for this plan (null = none). Set before Plan.</summary>
        public RunsFirstInputs Inputs { get; set; }

        /// <summary>Region cells the inputs took out of the mask (a report: wet cells counted once).</summary>
        public int BlockedCells { get; private set; }

        /// <summary>The open region cells after the inputs (a report).</summary>
        public int OpenCells { get; private set; }

        /// <summary>Area of the blocked region cells in km2 (0 with no input): the pick counts lines and lifts per open km2.</summary>
        private float BlockedKm2 => (float)(BlockedCells * (double)m_CellM2 / 1e6);
        private float m_CellM2;

        /// <summary>Takes the blocked cells out of the mask (the end of BuildGrid). No input: nothing changes.</summary>
        private void ApplyInputs(Grid g)
        {
            m_CellM2 = g.CellM * g.CellM;
            int open = 0;
            for (int c = 0; c < g.In.Length; c++)
                if (g.In[c])
                    open++;
            RunsFirstInputs inp = Inputs;
            if (inp == null || inp.Empty)
            {
                BlockedCells = 0;
                OpenCells = open;
                return;
            }
            int blocked = 0;
            if (inp.Water && g.S.Wet != null)
            {
                HeightSnapshot s = g.S;
                for (int cz = 0; cz < g.D; cz++)
                    for (int cx = 0; cx < g.W; cx++)
                    {
                        int c = cz * g.W + cx;
                        if (!g.In[c])
                            continue;
                        bool wet = false;
                        for (int dz = 0; dz < F && !wet; dz++)
                            for (int dx = 0; dx < F && !wet; dx++)
                                wet = s.Wet[(cz * F + dz) * s.Width + cx * F + dx] != 0;
                        if (wet)
                        {
                            g.In[c] = false;
                            blocked++;
                        }
                    }
                Poll();
            }
            foreach (RunsFirstInputs.Shape sh in inp.Blocked)
            {
                if (sh?.Pts == null || sh.Pts.Length == 0)
                    continue;
                blocked += BlockShape(g, sh.Pts, Math.Max(0f, sh.ClearM));
                Poll();
            }
            BlockedCells = blocked;
            OpenCells = open - blocked;
        }

        /// <summary>Takes the region cells whose center lies in the shape (or within clear of it) out of the mask. Returns their count.</summary>
        private static int BlockShape(Grid g, float2[] pts, float clear)
        {
            float2 lo = pts[0], hi = pts[0];
            for (int i = 1; i < pts.Length; i++)
            {
                lo = math.min(lo, pts[i]);
                hi = math.max(hi, pts[i]);
            }
            int x0 = Math.Max(0, (int)Math.Floor((lo.x - clear - g.OX) / g.CellM)), x1 = Math.Min(g.W - 1, (int)Math.Ceiling((hi.x + clear - g.OX) / g.CellM));
            int z0 = Math.Max(0, (int)Math.Floor((lo.y - clear - g.OZ) / g.CellM)), z1 = Math.Min(g.D - 1, (int)Math.Ceiling((hi.y + clear - g.OZ) / g.CellM));
            bool ring = pts.Length >= 3;
            float clear2 = clear * clear;
            int n = 0;
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    int c = z * g.W + x;
                    if (!g.In[c])
                        continue;
                    float2 p = g.World(c);
                    bool hit = ring && Inside(pts, p);
                    if (!hit && clear > 0f)
                        hit = ShapeDistanceSq(pts, p, clear2) <= clear2;
                    if (hit)
                    {
                        g.In[c] = false;
                        n++;
                    }
                }
            return n;
        }

        /// <summary>The squared distance from p to the shape's edges (a ring closes, a segment does not, a point is a point). Stops early
        /// once under stopAt.</summary>
        private static float ShapeDistanceSq(float2[] pts, float2 p, float stopAt)
        {
            if (pts.Length == 1)
                return math.distancesq(p, pts[0]);
            float best = float.PositiveInfinity;
            int edges = pts.Length >= 3 ? pts.Length : 1;
            for (int i = 0; i < edges && best > stopAt; i++)
            {
                float2 a = pts[i], b = pts[(i + 1) % pts.Length];
                float2 ab = b - a;
                float len2 = math.lengthsq(ab);
                float t = len2 > 0f ? math.saturate(math.dot(p - a, ab) / len2) : 0f;
                best = Math.Min(best, math.distancesq(p, a + t * ab));
            }
            return best;
        }
    }
}
