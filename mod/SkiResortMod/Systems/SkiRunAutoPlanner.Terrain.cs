// Resort Planner (v2) stage A: terrain: bands, pads, benches, ridges (spec 4.1). Stage Terrain, Resort mode only, between Minimax and
// PodRoute. Reads the v1 field m_O (the 20 m window grade, never re-sampled), m_H, m_Flow, m_Flags (Usable / Zones) and writes the
// Resort layers m_OS (3-texel box-smoothed grade, segmentation only: the rater never reads it), m_Band, m_PadOf, m_Ridge, plus the pad
// list m_PadList. A pad is a SITE the planner would later grade (stage B, the cat track grading kernel), not a leveled lot: under 25 %
// smoothed grade, at least PadMinAreaM2, an inscribed distance of PadMinInscribedM, and a cut / fill estimate for the cheapest
// station's lot at its station spot (PlanPad.GradeM3).
// Scratch (all rewritten before they are read again): m_Order = the union-find parents, then the component labels (TopRows refills it
// in Seeds); m_Dist[1] = the smoothed height (ridges), m_Dist[2] = the inscribed-distance chamfer (StartPassRows resets every m_Dist).
// Pure and Entities-free (usings System, System.Collections.Generic, Unity.Mathematics only). Spec: docs/research/quickruns-v2/spec/stage-a-spec.md.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiRunAutoPlanner
    {
        // ---------------------------------------------------------------- constants (spec 4.1; [B] 2.2, [C1] 3b)
        /// <summary>TerrainBand floors: Marginal 6 %, Beginner 8, Intermediate 25, Advanced 45, Expert 70, Cliff 100 % (Ecosign bands).</summary>
        public static readonly float[] TerrainBandFloor = { 0.06f, 0.08f, 0.25f, 0.45f, 0.70f, 1.00f };
        public const float PadMaxGrade = 0.25f, PadMinAreaM2 = 2000f, PadMinInscribedM = 9f;   // 0.2 ha; half the 16 m lot side + 1 m
        public const float PadLearningGrade = 0.08f, PadNoviceGrade = 0.15f;                    // PadKind: Flat / Learning / Novice
        public const int MaxPads = 255;                                                         // ids fit the byte layer (255 = none)
        public const byte PadNone = 255;
        /// <summary>Station lot half-diagonals by lift type (fixed grip, detachable, gondola, tram): lots 16x24, 16x32, 24x32, 48x24 m
        /// ([C1] table 3b; lot cell 8 m, DC/Game.Prefabs/BuildingInitializeSystem.cs:1266-1272).</summary>
        public static readonly float[] StationHalfDiagonalM = { 14.4f, 17.9f, 20.0f, 26.8f };
        /// <summary>The cheapest station's lot (fixed grip, 16 x 24 m): the lot the pad's cut / fill estimate levels.</summary>
        public const float PadLotShortM = 16f, PadLotLongM = 24f;

        /// <summary>Ecosign segmentation band of a grade (rise over run).</summary>
        public static TerrainBand BandOfGrade(float o)
        {
            int b = 0;
            while (b < TerrainBandFloor.Length && o >= TerrainBandFloor[b])
                b++;
            return (TerrainBand)b;
        }

        // ---------------------------------------------------------------- state
        // A pad-candidate component (8-connected usable cells with m_OS < PadMaxGrade), indexed by its compact label
        private struct PadComp
        {
            public int First, Cells, Best;    // smallest cell (the union-find root), cell count, the cell with the largest inscribed distance
            public double GradeSum;
            public float MinY, MaxY, Inscribed;
            public bool Base, Keep;
            public int Pad;                   // pad id, -1 = not a pad
        }

        private readonly List<PadComp> m_PadComps = new List<PadComp>();
        private readonly List<PlanPad> m_PadList = new List<PlanPad>();
        private readonly List<int> m_PadOrder = new List<int>();

        /// <summary>Pads of the last Resort scan (Terrain stage), in id order; empty in Runs mode. The plan copies them (TerrainPlan).</summary>
        public int PadCount => m_PadList.Count;

        /// <summary>Bytes of every per-cell buffer the planner holds (v1 lattice arrays, the four pass routes, the Resort layers, the
        /// flow sort counts): the P-4 memory gate. Lists of candidates and plan objects are not counted (they scale with runs, not cells).</summary>
        public long BufferBytes
        {
            get
            {
                long b = ResortLayerBytes + Len(m_Flags) + Len(m_Raw) * 2L + Len(m_M) * 2L + Len(m_InBand) + Len(m_ROver) + Len(m_APar) +
                         (Len(m_H) + Len(m_O) + Len(m_DHard) + Len(m_DEdge) + Len(m_DAcc) + Len(m_AG) + Len(m_Flow) + Len(m_Order) + Len(m_Count)) * 4L;
                for (int k = 0; k < Passes; k++)
                    b += Len(m_Pred[k]) + Len(m_Dist[k]) * 4L;
                return b;
            }
        }

        private static long Len(Array a) => a?.Length ?? 0;

        private bool PadCandidate(int c) =>
            (m_Flags[c] & (FOpen | FCliff)) == FOpen && m_OS[c] < PadMaxGrade;

        /// <summary>Stage Terrain (Resort only). m_Cursor2 phases: 0 setup; 1 rows: m_OS, m_Band, the smoothed height, layer reset;
        /// 2 rows: ridges and the pad-candidate union (W, NW, N, NE: every parent is an earlier cell); 3 rows: labels (one ascending
        /// flatten) and component sums; 4 chamfer seeds over the region bbox; 5 / 6 forward / backward chamfer; 7 rows: the inscribed
        /// maximum per component; 8 one unit: the pad list; 9 rows: m_PadOf. Then PodRoute.</summary>
        private void UnitTerrain()
        {
            switch (m_Cursor2)
            {
                case 0:
                    m_PadComps.Clear();
                    m_PadList.Clear();
                    m_Cursor = 0;
                    m_Cursor2 = 1;
                    return;
                case 1:
                    TerrainLayerRows();
                    return;
                case 2:
                    TerrainUnionRows();
                    return;
                case 3:
                    TerrainLabelRows();
                    return;
                case 4:
                    TerrainSeedRows();
                    return;
                case 5:
                case 6:
                    TerrainChamferRows(m_Cursor2 == 5);
                    return;
                case 7:
                    TerrainInscribedRows();
                    return;
                case 8:
                    BuildPads();
                    m_Cursor = m_RegionJ0;
                    m_Cursor2 = 9;
                    return;
                default:
                    TerrainPadRows();
                    return;
            }
        }

        // Phase 1, rows over the whole lattice: every Resort layer is reset here, so nothing of an earlier scan survives
        private void TerrainLayerRows()
        {
            float[] hs = m_Dist[1];
            int jEnd = Math.Min(m_Nz, m_Cursor + RowsPerUnit);
            for (int j = m_Cursor; j < jEnd; j++)
                for (int i = 0; i < m_Nx; i++)
                {
                    int c = j * m_Nx + i;
                    m_PadOf[c] = PadNone;
                    m_Ridge[c] = 0;
                    m_PodOf[c] = MUnreached;
                    float hSum = 0f, oSum = 0f;
                    int hN = 0, oN = 0;
                    for (int dj = -1; dj <= 1; dj++)
                    {
                        int nj = j + dj;
                        if (nj < 0 || nj >= m_Nz)
                            continue;
                        for (int di = -1; di <= 1; di++)
                        {
                            int ni = i + di;
                            if (ni < 0 || ni >= m_Nx)
                                continue;
                            int nc = nj * m_Nx + ni;
                            hSum += m_H[nc];
                            hN++;
                            if ((m_Flags[nc] & FOpen) != 0)
                            {
                                oSum += m_O[nc];
                                oN++;
                            }
                        }
                    }
                    hs[c] = hSum / hN;
                    bool open = (m_Flags[c] & FOpen) != 0;
                    m_OS[c] = open ? oSum / oN : float.NaN;
                    m_Band[c] = open ? (byte)BandOfGrade(m_O[c]) : PadNone;
                }
            m_Cursor = jEnd;
            if (m_Cursor < m_Nz)
                return;
            m_Cursor = m_RegionJ0;
            m_Cursor2 = 2;
        }

        // Phase 2, rows over the region bbox. Ridge = a usable cell with no D8 inflow (m_Flow is still its own area) that is a strict
        // local maximum of the smoothed height across at least one of the four axes. Pad candidates are unioned with their earlier
        // 8-neighbors (Root, Select.cs: the smaller root wins, so every parent is an earlier cell and a component's root is its smallest cell).
        private void TerrainUnionRows()
        {
            float[] hs = m_Dist[1];
            float own = 2f * m_Texel * m_Texel;
            int[] p = m_Order;
            int jEnd = Math.Min(m_RegionJ1 + 1, m_Cursor + RowsPerUnit);
            for (int j = m_Cursor; j < jEnd; j++)
                for (int i = m_RegionI0; i <= m_RegionI1; i++)
                {
                    int c = j * m_Nx + i;
                    if ((m_Flags[c] & (FOpen | FCliff)) != FOpen)
                        continue;
                    if (m_Flow[c] < own)
                    {
                        float h = hs[c];
                        if ((h > hs[c - 1] && h > hs[c + 1]) || (h > hs[c - m_Nx] && h > hs[c + m_Nx]) ||
                            (h > hs[c - m_Nx - 1] && h > hs[c + m_Nx + 1]) || (h > hs[c - m_Nx + 1] && h > hs[c + m_Nx - 1]))
                            m_Ridge[c] = 1;
                    }
                    if (!PadCandidate(c))
                        continue;
                    p[c] = c;
                    if (i > m_RegionI0)
                        UnionCell(p, c, c - 1);
                    if (j > m_RegionJ0)
                    {
                        int up = c - m_Nx;
                        UnionCell(p, c, up);
                        if (i > m_RegionI0)
                            UnionCell(p, c, up - 1);
                        if (i < m_RegionI1)
                            UnionCell(p, c, up + 1);
                    }
                }
            m_Cursor = jEnd;
            if (m_Cursor <= m_RegionJ1)
                return;
            m_Cursor = m_RegionJ0;
            m_Cursor2 = 3;
        }

        private void UnionCell(int[] p, int c, int n)
        {
            if (!PadCandidate(n))
                return;
            int a = Root(p, c), b = Root(p, n);
            if (a != b)
                p[Math.Max(a, b)] = Math.Min(a, b);
        }

        // Phase 3, rows ascending: a root becomes the next compact label -(k + 1); any other cell copies its parent's label (its parent
        // is an earlier cell, already flattened). Component sums ride along.
        private void TerrainLabelRows()
        {
            int[] p = m_Order;
            int jEnd = Math.Min(m_RegionJ1 + 1, m_Cursor + RowsPerUnit);
            for (int j = m_Cursor; j < jEnd; j++)
                for (int i = m_RegionI0; i <= m_RegionI1; i++)
                {
                    int c = j * m_Nx + i;
                    if (!PadCandidate(c))
                        continue;
                    int k;
                    if (p[c] == c)
                    {
                        k = m_PadComps.Count;
                        m_PadComps.Add(new PadComp { First = c, MinY = float.PositiveInfinity, MaxY = float.NegativeInfinity, Pad = -1, Best = -1 });
                        p[c] = -(k + 1);
                    }
                    else
                    {
                        p[c] = p[p[c]];
                        k = -p[c] - 1;
                    }
                    PadComp pc = m_PadComps[k];
                    pc.Cells++;
                    pc.GradeSum += m_OS[c];
                    pc.MinY = Math.Min(pc.MinY, m_H[c]);
                    pc.MaxY = Math.Max(pc.MaxY, m_H[c]);
                    if ((m_Flags[c] & FBase) != 0)
                        pc.Base = true;
                    m_PadComps[k] = pc;
                }
            m_Cursor = jEnd;
            if (m_Cursor <= m_RegionJ1)
                return;
            int minCells = (int)math.ceil(PadMinAreaM2 / (m_Texel * m_Texel));
            for (int k = 0; k < m_PadComps.Count; k++)
            {
                PadComp pc = m_PadComps[k];
                pc.Keep = pc.Cells >= minCells;
                m_PadComps[k] = pc;
            }
            m_Cursor = m_RegionJ0;
            m_Cursor2 = 4;
        }

        /// <summary>The compact component label of a pad-candidate cell after phase 3 (-1 for any other cell).</summary>
        private int CompOf(int c) => PadCandidate(c) ? -m_Order[c] - 1 : -1;

        // Phase 4: chamfer seeds over the region bbox: a cell of a kept component starts at +inf, every other cell at -half a texel
        // (the distance is measured to the pad's edge, the v1 DHard convention)
        private void TerrainSeedRows()
        {
            float[] d = m_Dist[2];
            int jEnd = Math.Min(m_RegionJ1 + 1, m_Cursor + RowsPerUnit);
            for (int j = m_Cursor; j < jEnd; j++)
                for (int i = m_RegionI0; i <= m_RegionI1; i++)
                {
                    int c = j * m_Nx + i;
                    int k = CompOf(c);
                    d[c] = k >= 0 && m_PadComps[k].Keep ? float.PositiveInfinity : -0.5f * m_Texel;
                }
            m_Cursor = jEnd;
            if (m_Cursor <= m_RegionJ1)
                return;
            m_Cursor = 0;
            m_Cursor2 = 5;
        }

        // Phases 5 / 6: the two chamfer passes over the region bbox in half row chunks (UnitDist's shape)
        private void TerrainChamferRows(bool forward)
        {
            int rows = Math.Max(1, RowsPerUnit / 2), j0 = m_RegionJ0, j1 = m_RegionJ1;
            int span = j1 - j0 + 1;
            if (forward)
            {
                int from = j0 + m_Cursor, to = Math.Min(j1, from + rows - 1);
                Chamfer(m_Dist[2], m_Nx, m_RegionI0, m_RegionI1, j0, j1, from, to, true, m_Texel);
                m_Cursor += to - from + 1;
            }
            else
            {
                int hi = j1 - m_Cursor, lo = Math.Max(j0, hi - rows + 1);
                Chamfer(m_Dist[2], m_Nx, m_RegionI0, m_RegionI1, j0, j1, lo, hi, false, m_Texel);
                m_Cursor += hi - lo + 1;
            }
            if (m_Cursor < span)
                return;
            m_Cursor = forward ? 0 : m_RegionJ0;
            m_Cursor2 = forward ? 6 : 7;
        }

        // Phase 7, rows: each kept component's largest inscribed distance and its cell (ties: the smaller cell, i.e. the first seen)
        private void TerrainInscribedRows()
        {
            float[] d = m_Dist[2];
            int jEnd = Math.Min(m_RegionJ1 + 1, m_Cursor + RowsPerUnit);
            for (int j = m_Cursor; j < jEnd; j++)
                for (int i = m_RegionI0; i <= m_RegionI1; i++)
                {
                    int c = j * m_Nx + i;
                    int k = CompOf(c);
                    if (k < 0 || !m_PadComps[k].Keep)
                        continue;
                    PadComp pc = m_PadComps[k];
                    if (pc.Best < 0 || d[c] > pc.Inscribed)
                    {
                        pc.Best = c;
                        pc.Inscribed = d[c];
                        m_PadComps[k] = pc;
                    }
                }
            m_Cursor = jEnd;
            if (m_Cursor <= m_RegionJ1)
                return;
            m_Cursor2 = 8;
        }

        // Phase 8, one unit: kept components with InscribedM >= PadMinInscribedM become pads; over MaxPads the largest by area stay
        // (ties: the smaller first cell); ids follow the smallest cell (the component order). IsBase = touches the v1 base band or is
        // the lowest pad; IsBench = not base and its lowest cell above the base band top.
        private void BuildPads()
        {
            m_PadOrder.Clear();
            m_PadFirst.Clear();
            for (int k = 0; k < m_PadComps.Count; k++)
                if (m_PadComps[k].Keep && m_PadComps[k].Inscribed >= PadMinInscribedM)
                    m_PadOrder.Add(k);
            if (m_PadOrder.Count > MaxPads)
            {
                m_PadOrder.Sort((a, b) =>
                {
                    int byArea = m_PadComps[b].Cells.CompareTo(m_PadComps[a].Cells);
                    return byArea != 0 ? byArea : m_PadComps[a].First.CompareTo(m_PadComps[b].First);
                });
                m_PadOrder.RemoveRange(MaxPads, m_PadOrder.Count - MaxPads);
                m_PadOrder.Sort((a, b) => m_PadComps[a].First.CompareTo(m_PadComps[b].First));
            }
            float cellArea = m_Texel * m_Texel;
            int lowest = -1;
            for (int n = 0; n < m_PadOrder.Count; n++)
            {
                int k = m_PadOrder[n];
                PadComp pc = m_PadComps[k];
                pc.Pad = n;
                m_PadComps[k] = pc;
                m_PadFirst.Add(pc.First);
                float mean = (float)(pc.GradeSum / pc.Cells);
                float2 center = CellCenter(pc.Best);
                PadLotGrade(pc.Best, out float gradeM3, out float gradeMaxM);
                m_PadList.Add(new PlanPad
                {
                    Id = n,
                    Kind = mean < PadLearningGrade ? PadKind.Flat : mean < PadNoviceGrade ? PadKind.Learning : PadKind.Novice,
                    AreaM2 = pc.Cells * cellArea, MeanGrade = mean, MinY = pc.MinY, MaxY = pc.MaxY,
                    Center = center, InscribedM = pc.Inscribed,
                    IsBase = pc.Base,
                    AccessM = AccessDistance(center),
                    GradeM3 = gradeM3, GradeMaxM = gradeMaxM,
                });
                if (lowest < 0 || pc.MinY < m_PadList[lowest].MinY)
                    lowest = n;
            }
            for (int n = 0; n < m_PadList.Count; n++)
            {
                PlanPad pad = m_PadList[n];
                pad.IsBase |= n == lowest;
                pad.IsBench = !pad.IsBase && pad.MinY > m_BaseTop;
                m_PadList[n] = pad;
            }
        }

        // Phase 9, rows: the pad id layer
        private void TerrainPadRows()
        {
            int jEnd = Math.Min(m_RegionJ1 + 1, m_Cursor + RowsPerUnit);
            for (int j = m_Cursor; j < jEnd; j++)
                for (int i = m_RegionI0; i <= m_RegionI1; i++)
                {
                    int c = j * m_Nx + i;
                    int k = CompOf(c);
                    if (k >= 0 && m_PadComps[k].Pad >= 0)
                        m_PadOf[c] = (byte)m_PadComps[k].Pad;
                }
            m_Cursor = jEnd;
            if (m_Cursor <= m_RegionJ1)
                return;
            Next(Stage.PodRoute);
        }

        /// <summary>Distance from a pad's station spot to the nearest access point (road samples, Welcome Signs); +inf when none.</summary>
        private float AccessDistance(float2 p)
        {
            float2[] acc = m_In.Access;
            float best = float.PositiveInfinity;
            if (acc == null)
                return best;
            for (int k = 0; k < acc.Length; k++)
                best = Math.Min(best, math.distance(p, acc[k]));
            return best;
        }

        /// <summary>Cut / fill estimate of grading the cheapest station's lot (PadLotShortM x PadLotLongM) level at the station spot: the
        /// level is the mean ground height under the lot (balanced, so cut = fill = volume), the lot's long side along x or z, whichever
        /// moves less earth. Volume in m3 and the deepest cut or fill in m. A screening number for stage B's grading kernel, not a plan.</summary>
        private void PadLotGrade(int c, out float volumeM3, out float maxM)
        {
            volumeM3 = float.PositiveInfinity;
            maxM = 0f;
            int ci = c % m_Nx, cj = c / m_Nx;
            float cellArea = m_Texel * m_Texel;
            for (int orient = 0; orient < 2; orient++)
            {
                float hx = 0.5f * (orient == 0 ? PadLotLongM : PadLotShortM), hz = 0.5f * (orient == 0 ? PadLotShortM : PadLotLongM);
                int ri = (int)math.floor(hx / m_Texel), rj = (int)math.floor(hz / m_Texel);
                double sum = 0.0;
                int n = 0;
                for (int j = Math.Max(0, cj - rj); j <= Math.Min(m_Nz - 1, cj + rj); j++)
                    for (int i = Math.Max(0, ci - ri); i <= Math.Min(m_Nx - 1, ci + ri); i++)
                    {
                        sum += m_H[j * m_Nx + i];
                        n++;
                    }
                float level = (float)(sum / Math.Max(1, n));
                double cut = 0.0;
                float deepest = 0f;
                for (int j = Math.Max(0, cj - rj); j <= Math.Min(m_Nz - 1, cj + rj); j++)
                    for (int i = Math.Max(0, ci - ri); i <= Math.Min(m_Nx - 1, ci + ri); i++)
                    {
                        float dh = m_H[j * m_Nx + i] - level;
                        if (dh > 0f)
                            cut += dh;
                        deepest = Math.Max(deepest, Math.Abs(dh));
                    }
                float vol = (float)(cut * cellArea);
                if (vol < volumeM3)
                {
                    volumeM3 = vol;
                    maxM = deepest;
                }
            }
        }
    }
}
