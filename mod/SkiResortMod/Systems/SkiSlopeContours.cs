// T253 (1.0.4): smooth band outlines for the slope overlay and the run heat-map. Pure C# (Unity.Mathematics only), no Game types,
// so SlopeBatch tests it and renders the offline preview with the same code.
//
// Input: a grid of cell values o (SkiSlopeClassifier.CellValue, the 20 m window grade) at cell centers, NaN = no value. Output:
// - Fill: one row strip per band interval of each row. The strip ends sit where the row's piecewise-linear o crosses a band limit, not
//   on cell borders, so the fill has no x staircase.
// - Curves: the band limits as contour lines (marching squares on the cell-center grid, linear interpolation), smoothed (Chaikin), simplified
//   (Douglas-Peucker) and turned into cubic Beziers (Catmull-Rom). Each Bezier is one OverlayRenderSystem.Buffer.DrawCurve call.
// Display only: a 3x3 binomial blur of o (NaN-aware) runs first and removes single-cell speckle. The rating and every number stay raw.
// Engine notes: EngineNotes/SkiSlopeContours.md

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed class SkiSlopeContours
    {
        public struct Curve
        {
            public float3 P0, P1, P2, P3;   // cubic Bezier control points (world m)
            public byte KeyLo, KeyHi;       // strip keys on the gentle and the steep side of the limit
            public byte Fade;               // 1..FadeLevels (disk rim), FadeLevels without a disk
            public float YLo, YHi;          // draw box (m)
        }

        public struct FillRun
        {
            public int J;                   // row index in the input grid
            public double X0, X1;           // world x of the ends (m)
            public double Z;                // world z of the row center (m)
            public float Width;             // row height (m) = step
            public byte Key, Fade;
            public float Y, YLo, YHi;       // strip height, draw box (m)
        }

        public const int FadeLevels = 4;
        public const byte NoKey = 255;

        // Tuning (the preview varies them; the game uses the defaults).
        public bool Blur = true;
        public int ChaikinPasses = 2;
        public float SimplifyM = 0.2f;
        public float MinLoopM = 12f;
        public float MinOpenM = 1f;
        public float BoxMarginM = 8f;
        public bool DrawCurves = true;

        // Optional disk (the slope overlay): fill clipped to the circle, fade over the outer FadeM in FadeLevels steps.
        public bool Disk;
        public double DiskCx, DiskCz;
        public float DiskR, FadeM;

        // Optional polygon (the run heat-map): the fill ends exactly on the polygon (even-odd rule) instead of half a cell past the last
        // cell center inside it. For the fill only, the field is grown by one cell ring (mean of the valid 8 neighbors), so the fill
        // reaches the polygon edge between cell centers. The curves come from the field as given.
        public double2[] ClipRing;
        public int ClipCount;

        public readonly List<Curve> Curves = new List<Curve>();
        public readonly List<FillRun> Fill = new List<FillRun>();
        public int Segments, Polylines, LoopsDropped;

        private float[] m_F, m_NumA, m_DenA, m_NumB, m_DenB, m_H;
        private int m_Nx, m_Nz;
        private double m_Ox, m_Oz, m_Step;
        private bool m_Eu;
        private int[] m_EdgeSegA, m_EdgeSegB;
        private readonly List<int> m_TouchedEdges = new List<int>();
        private readonly List<int2> m_Seg = new List<int2>();
        private bool[] m_SegUsed = new bool[0];
        private readonly List<int> m_Chain = new List<int>();
        private readonly List<int> m_Back = new List<int>();
        private readonly List<double2> m_Pts = new List<double2>();
        private readonly List<double2> m_Tmp = new List<double2>();
        private readonly List<double2> m_Simple = new List<double2>();
        private readonly Stack<int2> m_Dp = new Stack<int2>();
        private bool[] m_Keep = new bool[0];
        private float[] m_Levels = new float[0];
        private readonly List<double> m_Cuts = new List<double>();

        /// <summary>The band limits in increasing order: 0.06, 0.25, 0.40, 0.60, plus 0.15 (the European green / blue split).</summary>
        public static float[] Limits(bool eu) => eu
            ? new[] { SkiSlopeClassifier.GradeGreen, DisplayUnits.EuGreenMaxPct / 100f, SkiSlopeClassifier.GradeBlue,
                      SkiSlopeClassifier.GradeBlack, SkiSlopeClassifier.GradeDoubleBlack }
            : new[] { SkiSlopeClassifier.GradeGreen, SkiSlopeClassifier.GradeBlue, SkiSlopeClassifier.GradeBlack,
                      SkiSlopeClassifier.GradeDoubleBlack };

        /// <summary>The strip key of a value (DisplayUnits.StripKey of its band), NoKey for NaN.</summary>
        public static byte KeyOf(float v, bool eu) =>
            float.IsNaN(v) ? NoKey : DisplayUnits.StripKey((byte)SkiSlopeClassifier.BandOf(v), v, eu);

        /// <summary>The shown (blurred) value of cell (i, j) after Build; NaN outside.</summary>
        public float Shown(int i, int j) => i < 0 || j < 0 || i >= m_Nx || j >= m_Nz ? float.NaN : m_F[j * m_Nx + i];

        /// <summary>o and h: nx x nz, index j * nx + i, cell (i, j) centered at (ox + i * step, oz + j * step). h may be null (y = 0,
        /// no boxes).</summary>
        public void Build(float[] o, float[] h, int nx, int nz, double ox, double oz, double step, bool eu)
        {
            Curves.Clear();
            Fill.Clear();
            Segments = Polylines = LoopsDropped = 0;
            if (o == null || nx < 1 || nz < 1 || o.Length < nx * nz || !(step > 0))
                return;
            int n = nx * nz;
            m_Nx = nx;
            m_Nz = nz;
            m_Ox = ox;
            m_Oz = oz;
            m_Step = step;
            m_Eu = eu;
            m_H = h != null && h.Length >= n ? h : null;
            if (m_F == null || m_F.Length < n)
            {
                m_F = new float[n];
                m_NumA = new float[n];
                m_DenA = new float[n];
                m_NumB = new float[n];
                m_DenB = new float[n];
                m_EdgeSegA = new int[2 * n];
                m_EdgeSegB = new int[2 * n];
                for (int q = 0; q < 2 * n; q++)
                    m_EdgeSegA[q] = m_EdgeSegB[q] = -1;
            }
            if (Blur)
                BlurInto(o);
            else
                Array.Copy(o, m_F, n);
            m_Levels = Limits(eu);
            m_G = m_F;
            if (ClipRing != null && ClipCount >= 3)
            {
                if (m_GrowBuf == null || m_GrowBuf.Length < n)
                    m_GrowBuf = new float[n];
                GrowRing(m_F, m_GrowBuf);
                m_G = m_GrowBuf;
            }
            m_CF = m_G;   // with a polygon the curves also come from the grown field and are clipped to the polygon
            BuildFill();
            if (DrawCurves)
                for (int k = 0; k < m_Levels.Length; k++)
                    BuildLevel(m_Levels[k]);
        }

        // ---- display blur: 3x3 binomial (1-2-1 x 1-2-1), NaN-aware, NaN stays NaN ----
        private void BlurInto(float[] o)
        {
            int nx = m_Nx, nz = m_Nz;
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    float num = 0f, den = 0f;
                    for (int d = -1; d <= 1; d++)
                    {
                        int ii = i + d;
                        if (ii < 0 || ii >= nx)
                            continue;
                        float v = o[j * nx + ii];
                        if (float.IsNaN(v))
                            continue;
                        float w = d == 0 ? 2f : 1f;
                        num += w * v;
                        den += w;
                    }
                    m_NumA[j * nx + i] = num;
                    m_DenA[j * nx + i] = den;
                }
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    int q = j * nx + i;
                    if (float.IsNaN(o[q]))
                    {
                        m_F[q] = float.NaN;
                        continue;
                    }
                    float num = 0f, den = 0f;
                    for (int d = -1; d <= 1; d++)
                    {
                        int jj = j + d;
                        if (jj < 0 || jj >= nz)
                            continue;
                        float w = d == 0 ? 2f : 1f;
                        num += w * m_NumA[jj * nx + i];
                        den += w * m_DenA[jj * nx + i];
                    }
                    m_F[q] = den > 0f ? num / den : o[q];
                }
        }

        // ---- fill: per row, the piecewise-linear value through the cell centers, cut at every band limit ----
        // A strip as tall as the row overshoots a band edge that runs at a shallow angle to x by up to half a row. Such a row is drawn as
        // SubRows thin strips instead, each one ending at the crossing on its own line (the field interpolated linearly between the rows).
        // Where all sub-lines agree (within SnapM of the row's crossing), one full-height strip is kept.
        private struct Brk
        {
            public double X;
            public byte Left, Right;        // keys on both sides, NoKey = no value
        }

        public int SubRows = 4;
        public float SnapM = 0.5f;
        public int SubStrips;               // strips of sub-row height in the last Build

        private List<Brk>[] m_Brk = new List<Brk>[0];
        private float[] m_Line = new float[0];
        private byte[] m_KeyOfLevel = new byte[0];
        private int[] m_Ptr = new int[0];
        private byte[] m_SubKey = new byte[0], m_SubOpenKey = new byte[0];
        private double[] m_SubX0 = new double[0];
        private readonly List<double> m_Xs = new List<double>();

        private int LevelOf(float v)
        {
            int k = 0;
            while (k < m_Levels.Length && v >= m_Levels[k])
                k++;
            return k;
        }

        private void BuildFill()
        {
            int nx = m_Nx, nz = m_Nz, subs = Math.Max(1, SubRows);
            double step = m_Step;
            if (m_KeyOfLevel.Length != m_Levels.Length + 1)
                m_KeyOfLevel = new byte[m_Levels.Length + 1];
            for (int k = 0; k <= m_Levels.Length; k++)
            {
                float lo = k == 0 ? 0f : m_Levels[k - 1], hi = k == m_Levels.Length ? lo + 1f : m_Levels[k];
                m_KeyOfLevel[k] = KeyOf(0.5f * (lo + hi), m_Eu);
            }
            if (m_Brk.Length != subs + 1)
            {
                m_Brk = new List<Brk>[subs + 1];
                for (int s = 0; s <= subs; s++)
                    m_Brk[s] = new List<Brk>();
                m_Ptr = new int[subs];
                m_SubKey = new byte[subs];
                m_SubOpenKey = new byte[subs];
                m_SubX0 = new double[subs];
            }
            if (m_Line.Length < nx)
                m_Line = new float[nx];
            SubStrips = 0;
            for (int j = 0; j < nz; j++)
            {
                double zc = m_Oz + j * step;
                if (Disk && Math.Abs(zc - DiskCz) > DiskR + step)
                    continue;
                int row = j * nx;
                LineBreaks(m_G, row, m_Brk[0], zc);
                bool same = true;
                if (subs > 1)
                    for (int s = 0; s < subs; s++)
                    {
                        double off = (s + 0.5) / subs - 0.5;        // in rows, -0.375 .. 0.375 for 4
                        int jj = j + (off < 0 ? -1 : 1);
                        float w = (float)Math.Abs(off);
                        for (int i = 0; i < nx; i++)
                        {
                            float a = m_G[row + i];
                            float b = jj >= 0 && jj < nz ? m_G[jj * nx + i] : float.NaN;
                            m_Line[i] = float.IsNaN(a) || float.IsNaN(b) ? a : a + w * (b - a);
                        }
                        List<Brk> l = m_Brk[s + 1];
                        LineBreaks(m_Line, 0, l, zc + off * step);
                        Snap(l, m_Brk[0]);
                        same &= SameBreaks(l, m_Brk[0]);
                    }
                if (same)
                {
                    EmitLine(j, zc, step, m_Brk[0]);
                    continue;
                }
                SweepSubLines(j, zc, step, subs);
            }
        }

        /// <summary>Breakpoints of one line of values at the cell centers: span starts and ends (half a cell out) and band crossings.</summary>
        private void LineBreaks(float[] v, int at, List<Brk> outB, double z)
        {
            ValueBreaks(v, at, outB);
            if (ClipRing != null && ClipCount >= 3)
                ApplyClip(outB, z);
        }

        private float[] m_G, m_GrowBuf, m_CF;   // fill field, its grown copy, contour field
        private readonly List<double> m_PolyX = new List<double>(), m_Cand = new List<double>();
        private readonly List<Brk> m_BrkTmp = new List<Brk>();

        private void GrowRing(float[] src, float[] dst)
        {
            int nx = m_Nx, nz = m_Nz;
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    int q = j * nx + i;
                    float v = src[q];
                    if (!float.IsNaN(v))
                    {
                        dst[q] = v;
                        continue;
                    }
                    float sum = 0f;
                    int cnt = 0;
                    for (int dj = -1; dj <= 1; dj++)
                        for (int di = -1; di <= 1; di++)
                        {
                            int ii = i + di, jj = j + dj;
                            if (ii < 0 || jj < 0 || ii >= nx || jj >= nz)
                                continue;
                            float u = src[jj * nx + ii];
                            if (float.IsNaN(u))
                                continue;
                            sum += u;
                            cnt++;
                        }
                    dst[q] = cnt > 0 ? sum / cnt : float.NaN;
                }
        }

        /// <summary>Intersects a line's breakpoints with the polygon's inside intervals on the line z (even-odd).</summary>
        private void ApplyClip(List<Brk> b, double z)
        {
            m_PolyX.Clear();
            for (int k = 0; k < ClipCount; k++)
            {
                double2 p = ClipRing[k], q = ClipRing[(k + 1) % ClipCount];
                if ((p.y <= z) == (q.y <= z))
                    continue;
                m_PolyX.Add(p.x + (z - p.y) / (q.y - p.y) * (q.x - p.x));
            }
            m_PolyX.Sort();
            m_Cand.Clear();
            foreach (Brk k in b)
                m_Cand.Add(k.X);
            m_Cand.AddRange(m_PolyX);
            m_Cand.Sort();
            m_BrkTmp.Clear();
            byte prev = NoKey;
            int pv = 0, pp = 0;
            for (int k = 0; k + 1 < m_Cand.Count; k++)
            {
                double xa = m_Cand[k], xb = m_Cand[k + 1];
                if (!(xb - xa > 1e-9))
                    continue;
                double xm = 0.5 * (xa + xb);
                while (pv < b.Count && b[pv].X <= xm)
                    pv++;
                while (pp < m_PolyX.Count && m_PolyX[pp] <= xm)
                    pp++;
                byte key = (pp & 1) == 1 && pv > 0 ? b[pv - 1].Right : NoKey;
                if (key != prev)
                {
                    m_BrkTmp.Add(new Brk { X = xa, Left = prev, Right = key });
                    prev = key;
                }
            }
            if (prev != NoKey && m_Cand.Count > 0)
                m_BrkTmp.Add(new Brk { X = m_Cand[m_Cand.Count - 1], Left = prev, Right = NoKey });
            b.Clear();
            b.AddRange(m_BrkTmp);
        }

        private void ValueBreaks(float[] v, int at, List<Brk> outB)
        {
            outB.Clear();
            int nx = m_Nx;
            double step = m_Step, half = 0.5 * step;
            int i = 0;
            while (i < nx)
            {
                if (float.IsNaN(v[at + i]))
                {
                    i++;
                    continue;
                }
                int s = i;
                while (i + 1 < nx && !float.IsNaN(v[at + i + 1]))
                    i++;
                int e = i;
                i++;
                int lv = LevelOf(v[at + s]);
                byte key = m_KeyOfLevel[lv];
                outB.Add(new Brk { X = m_Ox + s * step - half, Left = NoKey, Right = key });
                for (int c = s; c < e; c++)
                {
                    float va = v[at + c], vb = v[at + c + 1];
                    int lb = LevelOf(vb);
                    if (lb == lv)
                        continue;
                    double xa = m_Ox + c * step;
                    int dir = lb > lv ? 1 : -1;
                    for (int k = lv; k != lb; k += dir)
                    {
                        float lim = m_Levels[dir > 0 ? k : k - 1];
                        byte next = m_KeyOfLevel[k + dir];
                        if (next == key)
                            continue;
                        double x = xa + step * (lim - va) / (vb - va);
                        outB.Add(new Brk { X = x, Left = key, Right = next });
                        key = next;
                    }
                    lv = lb;
                }
                outB.Add(new Brk { X = m_Ox + e * step + half, Left = key, Right = NoKey });
            }
        }

        private void Snap(List<Brk> sub, List<Brk> center)
        {
            int p = 0;
            for (int k = 0; k < sub.Count; k++)
            {
                Brk b = sub[k];
                if (ClipRing != null && (b.Left == NoKey || b.Right == NoKey))
                    continue;                        // a polygon edge: exact on every sub-line (no outline covers it)
                while (p < center.Count && center[p].X < b.X - SnapM)
                    p++;
                for (int q = p; q < center.Count && center[q].X <= b.X + SnapM; q++)
                    if (center[q].Left == b.Left && center[q].Right == b.Right)
                    {
                        b.X = center[q].X;
                        sub[k] = b;
                        break;
                    }
            }
        }

        private static bool SameBreaks(List<Brk> a, List<Brk> b)
        {
            if (a.Count != b.Count)
                return false;
            for (int k = 0; k < a.Count; k++)
                if (a[k].X != b[k].X || a[k].Left != b[k].Left || a[k].Right != b[k].Right)
                    return false;
            return true;
        }

        private void EmitLine(int j, double z, double width, List<Brk> l)
        {
            for (int k = 0; k + 1 < l.Count; k++)
                if (l[k].Right != NoKey)
                    AddFill(j, z, width, l[k].X, l[k + 1].X, l[k].Right);
        }

        /// <summary>Sweeps the sub-lines' breakpoints: full-height strips where all sub-lines have the same key, thin strips elsewhere.</summary>
        private void SweepSubLines(int j, double zc, double step, int subs)
        {
            m_Xs.Clear();
            for (int s = 1; s <= subs; s++)
                foreach (Brk b in m_Brk[s])
                    m_Xs.Add(b.X);
            m_Xs.Sort();
            double hSub = step / subs;
            byte fullKey = NoKey;
            double fullX0 = 0;
            for (int s = 0; s < subs; s++)
            {
                m_Ptr[s] = 0;
                m_SubOpenKey[s] = NoKey;
            }
            for (int k = 0; k + 1 < m_Xs.Count; k++)
            {
                double xa = m_Xs[k], xb = m_Xs[k + 1];
                if (!(xb - xa > 1e-9))
                    continue;
                double xm = 0.5 * (xa + xb);
                bool all = true;
                for (int s = 0; s < subs; s++)
                {
                    List<Brk> l = m_Brk[s + 1];
                    int p = m_Ptr[s];
                    while (p < l.Count && l[p].X <= xm)
                        p++;
                    m_Ptr[s] = p;
                    m_SubKey[s] = p > 0 ? l[p - 1].Right : NoKey;
                    all &= m_SubKey[s] == m_SubKey[0];
                }
                if (all && m_SubKey[0] != NoKey)
                {
                    for (int s = 0; s < subs; s++)
                        CloseSub(j, zc, step, hSub, s, xa);
                    if (fullKey != m_SubKey[0])
                    {
                        if (fullKey != NoKey)
                            AddFill(j, zc, step, fullX0, xa, fullKey);
                        fullKey = m_SubKey[0];
                        fullX0 = xa;
                    }
                    continue;
                }
                if (fullKey != NoKey)
                {
                    AddFill(j, zc, step, fullX0, xa, fullKey);
                    fullKey = NoKey;
                }
                for (int s = 0; s < subs; s++)
                {
                    if (m_SubOpenKey[s] == m_SubKey[s])
                        continue;
                    CloseSub(j, zc, step, hSub, s, xa);
                    m_SubOpenKey[s] = m_SubKey[s];
                    m_SubX0[s] = xa;
                }
            }
            double xEnd = m_Xs.Count > 0 ? m_Xs[m_Xs.Count - 1] : 0;
            if (fullKey != NoKey)
                AddFill(j, zc, step, fullX0, xEnd, fullKey);
            for (int s = 0; s < subs; s++)
                CloseSub(j, zc, step, hSub, s, xEnd);
        }

        private void CloseSub(int j, double zc, double step, double hSub, int s, double x)
        {
            if (m_SubOpenKey[s] == NoKey)
                return;
            int before = Fill.Count;
            AddFill(j, zc + ((s + 0.5) / m_SubOpenKey.Length - 0.5) * step, hSub, m_SubX0[s], x, m_SubOpenKey[s]);
            SubStrips += Fill.Count - before;
            m_SubOpenKey[s] = NoKey;
        }

        private void AddFill(int j, double z, double width, double x0, double x1, byte key)
        {
            if (key == NoKey)
                return;
            if (Disk)
            {
                double dz0 = z - DiskCz, r2 = (double)DiskR * DiskR - dz0 * dz0;
                if (r2 <= 0)
                    return;
                double hw0 = Math.Sqrt(r2);
                x0 = Math.Max(x0, DiskCx - hw0);
                x1 = Math.Min(x1, DiskCx + hw0);
            }
            if (!(x1 - x0 > 1e-3))
                return;
            if (!Disk)
            {
                AddFillPiece(j, z, width, x0, x1, key, FadeLevels);
                return;
            }
            // Cut at the fade rings (radii R - FadeM * k / FadeLevels, k = 1 .. FadeLevels - 1), fade by each piece's center.
            m_Cuts.Clear();
            m_Cuts.Add(x0);
            double dz = z - DiskCz;
            for (int k = 1; k < FadeLevels; k++)
            {
                double r = DiskR - FadeM * k / FadeLevels, r2 = r * r - dz * dz;
                if (r2 <= 0)
                    continue;
                double hw = Math.Sqrt(r2);
                if (DiskCx - hw > x0 && DiskCx - hw < x1)
                    m_Cuts.Add(DiskCx - hw);
                if (DiskCx + hw > x0 && DiskCx + hw < x1)
                    m_Cuts.Add(DiskCx + hw);
            }
            m_Cuts.Add(x1);
            m_Cuts.Sort();
            for (int c = 0; c + 1 < m_Cuts.Count; c++)
            {
                double a = m_Cuts[c], b = m_Cuts[c + 1];
                if (!(b - a > 1e-3))
                    continue;
                double mx = 0.5 * (a + b) - DiskCx;
                byte fade = FadeOf(DiskR - Math.Sqrt(mx * mx + dz * dz));
                AddFillPiece(j, z, width, a, b, key, fade);
            }
        }

        private void AddFillPiece(int j, double z, double width, double x0, double x1, byte key, byte fade)
        {
            var r = new FillRun { J = j, X0 = x0, X1 = x1, Z = z, Width = (float)width, Key = key, Fade = fade };
            if (m_H != null)
            {
                r.Y = 0.5f * (HeightAt(x0, z) + HeightAt(x1, z));
                Box(x0 - m_Step, z - m_Step, x1 + m_Step, z + m_Step, out r.YLo, out r.YHi);
            }
            else
            {
                r.YLo = 1f;
                r.YHi = 0f;
            }
            Fill.Add(r);
        }

        private byte FadeOf(double edgeM)
        {
            if (!(FadeM > 0f) || edgeM >= FadeM)
                return FadeLevels;
            if (edgeM <= 0)
                return 1;
            int lv = 1 + (int)(edgeM / (FadeM / FadeLevels));
            return (byte)Math.Min(FadeLevels, Math.Max(1, lv));
        }

        // ---- contours: marching squares at one limit, linked into polylines ----
        private int EdgeH(int i, int j) => 2 * (j * m_Nx + i);
        private int EdgeV(int i, int j) => 2 * (j * m_Nx + i) + 1;

        private void BuildLevel(float lim)
        {
            int nx = m_Nx, nz = m_Nz;
            m_Seg.Clear();
            for (int j = 0; j + 1 < nz; j++)
                for (int i = 0; i + 1 < nx; i++)
                {
                    float a = m_CF[j * nx + i], b = m_CF[j * nx + i + 1], c = m_CF[(j + 1) * nx + i + 1], d = m_CF[(j + 1) * nx + i];
                    if (float.IsNaN(a) || float.IsNaN(b) || float.IsNaN(c) || float.IsNaN(d))
                        continue;
                    int idx = (a >= lim ? 1 : 0) | (b >= lim ? 2 : 0) | (c >= lim ? 4 : 0) | (d >= lim ? 8 : 0);
                    if (idx == 0 || idx == 15)
                        continue;
                    int e0 = EdgeH(i, j), e1 = EdgeV(i + 1, j), e2 = EdgeH(i, j + 1), e3 = EdgeV(i, j);
                    bool center = 0.25f * (a + b + c + d) >= lim;
                    switch (idx)
                    {
                        case 1: case 14: AddSeg(e3, e0); break;
                        case 2: case 13: AddSeg(e0, e1); break;
                        case 3: case 12: AddSeg(e3, e1); break;
                        case 4: case 11: AddSeg(e1, e2); break;
                        case 6: case 9: AddSeg(e0, e2); break;
                        case 7: case 8: AddSeg(e3, e2); break;
                        case 5:
                            if (center) { AddSeg(e0, e1); AddSeg(e2, e3); }
                            else { AddSeg(e3, e0); AddSeg(e1, e2); }
                            break;
                        case 10:
                            if (center) { AddSeg(e3, e0); AddSeg(e1, e2); }
                            else { AddSeg(e0, e1); AddSeg(e2, e3); }
                            break;
                    }
                }
            Segments += m_Seg.Count;
            if (m_SegUsed.Length < m_Seg.Count)
                m_SegUsed = new bool[Math.Max(m_Seg.Count, 2 * m_SegUsed.Length)];
            Array.Clear(m_SegUsed, 0, m_Seg.Count);
            byte keyHi = KeyOf(lim, m_Eu), keyLo = KeyOf(lim - 1e-4f, m_Eu);
            for (int s = 0; s < m_Seg.Count; s++)
            {
                if (m_SegUsed[s])
                    continue;
                bool closed = Walk(s);
                EmitPolyline(lim, closed, keyLo, keyHi);
            }
            foreach (int e in m_TouchedEdges)
                m_EdgeSegA[e] = m_EdgeSegB[e] = -1;
            m_TouchedEdges.Clear();
        }

        private void AddSeg(int ea, int eb)
        {
            int s = m_Seg.Count;
            m_Seg.Add(new int2(ea, eb));
            Link(ea, s);
            Link(eb, s);
        }

        private void Link(int e, int s)
        {
            if (m_EdgeSegA[e] < 0)
            {
                m_EdgeSegA[e] = s;
                m_TouchedEdges.Add(e);
            }
            else
                m_EdgeSegB[e] = s;
        }

        private int Other(int e, int s) => m_EdgeSegA[e] == s ? m_EdgeSegB[e] : m_EdgeSegA[e];

        /// <summary>Collects the edge chain through segment s0 into m_Chain (in order). True when it closes.</summary>
        private bool Walk(int s0)
        {
            m_Chain.Clear();
            m_Back.Clear();
            m_SegUsed[s0] = true;
            int2 g = m_Seg[s0];
            m_Chain.Add(g.x);
            m_Chain.Add(g.y);
            // Forward from g.y.
            int e = g.y, s = s0;
            while (true)
            {
                int nxt = Other(e, s);
                if (nxt < 0)
                    break;
                if (nxt == s0)
                    return true;   // closed: the last edge equals the first one (g.x)
                m_SegUsed[nxt] = true;
                int2 h = m_Seg[nxt];
                e = h.x == e ? h.y : h.x;
                s = nxt;
                m_Chain.Add(e);
            }
            // Backward from g.x.
            e = g.x;
            s = s0;
            while (true)
            {
                int nxt = Other(e, s);
                if (nxt < 0 || m_SegUsed[nxt])
                    break;
                m_SegUsed[nxt] = true;
                int2 h = m_Seg[nxt];
                e = h.x == e ? h.y : h.x;
                s = nxt;
                m_Back.Add(e);
            }
            if (m_Back.Count > 0)
            {
                m_Back.Reverse();
                m_Back.AddRange(m_Chain);
                m_Chain.Clear();
                m_Chain.AddRange(m_Back);
            }
            return false;
        }

        private double2 EdgePoint(int e, float lim)
        {
            int cell = e >> 1;
            int i = cell % m_Nx, j = cell / m_Nx;
            bool vertical = (e & 1) != 0;
            int i2 = vertical ? i : i + 1, j2 = vertical ? j + 1 : j;
            float va = m_CF[j * m_Nx + i], vb = m_CF[j2 * m_Nx + i2];
            double t = vb != va ? (lim - va) / (double)(vb - va) : 0.5;
            t = Math.Max(0.0, Math.Min(1.0, t));
            double x = m_Ox + (i + (vertical ? 0 : t)) * m_Step, z = m_Oz + (j + (vertical ? t : 0)) * m_Step;
            return new double2(x, z);
        }

        private void EmitPolyline(float lim, bool closed, byte keyLo, byte keyHi)
        {
            m_Pts.Clear();
            int count = m_Chain.Count;
            for (int k = 0; k < count; k++)
                m_Pts.Add(EdgePoint(m_Chain[k], lim));
            if (closed && m_Pts.Count > 1)
                m_Pts.RemoveAt(m_Pts.Count - 1);     // the walk ends on its first edge again
            if (HasClip && !AllInside())
            {
                ClipPieces(closed, keyLo, keyHi);
                return;
            }
            Finish(closed, keyLo, keyHi);
        }

        private bool HasClip => Disk || (ClipRing != null && ClipCount >= 3);

        /// <summary>Inside the disk, or inside the polygon (even-odd).</summary>
        private bool InsideClip(double2 p)
        {
            if (Disk)
            {
                double dx = p.x - DiskCx, dz = p.y - DiskCz;
                return dx * dx + dz * dz <= (double)DiskR * DiskR;
            }
            bool inside = false;
            for (int k = 0, m = ClipCount - 1; k < ClipCount; m = k++)
            {
                double2 a = ClipRing[k], b = ClipRing[m];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }

        /// <summary>Where the segment a -> b leaves (leaving = true: the first crossing) or enters (the last crossing) the clip region.</summary>
        private double2 ClipCross(double2 a, double2 b, bool leaving)
        {
            double2 d = b - a;
            if (Disk)
            {
                double2 f = a - new double2(DiskCx, DiskCz);
                double r2 = (double)DiskR * DiskR;
                double qa = math.dot(d, d), qb = 2 * math.dot(f, d), qc = math.dot(f, f) - r2;
                double disc = Math.Sqrt(Math.Max(0, qb * qb - 4 * qa * qc));
                double t = qa > 0 ? (leaving ? (-qb + disc) / (2 * qa) : (-qb - disc) / (2 * qa)) : 0;
                return a + math.clamp(t, 0.0, 1.0) * d;
            }
            double best = leaving ? 2.0 : -1.0;
            for (int k = 0; k < ClipCount; k++)
            {
                double2 p = ClipRing[k], e = ClipRing[(k + 1) % ClipCount] - p;
                double den = d.x * e.y - d.y * e.x;
                if (Math.Abs(den) < 1e-12)
                    continue;
                double2 ap = p - a;
                double t = (ap.x * e.y - ap.y * e.x) / den, u = (ap.x * d.y - ap.y * d.x) / den;
                if (t < 0 || t > 1 || u < 0 || u > 1)
                    continue;
                if (leaving ? t < best : t > best)
                    best = t;
            }
            if (best < 0 || best > 1)
                best = 0.5;
            return a + best * d;
        }

        private bool AllInside()
        {
            foreach (double2 p in m_Pts)
                if (!InsideClip(p))
                    return false;
            return true;
        }

        private readonly List<double2> m_Raw = new List<double2>();

        /// <summary>Splits m_Pts at the disk rim or the polygon into open pieces inside it (the ends exactly on the clip edge), and finishes
        /// each.</summary>
        private void ClipPieces(bool closed, byte keyLo, byte keyHi)
        {
            m_Raw.Clear();
            int n = m_Pts.Count, start = 0;
            if (closed)
            {
                while (start < n && InsideClip(m_Pts[start]))
                    start++;                         // start the walk outside, so no piece wraps around the start
                for (int k = 0; k <= n; k++)
                    m_Raw.Add(m_Pts[(start + k) % n]);
            }
            else
                m_Raw.AddRange(m_Pts);
            m_Pts.Clear();
            bool inside = InsideClip(m_Raw[0]);
            if (inside)
                m_Pts.Add(m_Raw[0]);
            for (int k = 1; k < m_Raw.Count; k++)
            {
                double2 a = m_Raw[k - 1], b = m_Raw[k];
                bool bIn = InsideClip(b);
                if (bIn != inside)
                {
                    double2 x = ClipCross(a, b, inside);
                    m_Pts.Add(x);
                    if (inside)
                    {
                        Finish(false, keyLo, keyHi);
                        m_Pts.Clear();
                    }
                    inside = bIn;
                }
                if (inside)
                    m_Pts.Add(b);
            }
            if (inside && m_Pts.Count > 1)
                Finish(false, keyLo, keyHi);
        }

        private void Finish(bool closed, byte keyLo, byte keyHi)
        {
            double len = 0;
            for (int k = 0; k + 1 < m_Pts.Count; k++)
                len += math.distance(m_Pts[k], m_Pts[k + 1]);
            if (closed)
                len += math.distance(m_Pts[m_Pts.Count - 1], m_Pts[0]);
            if (closed ? len < MinLoopM : len < MinOpenM)
            {
                LoopsDropped++;
                return;
            }
            Polylines++;
            for (int p = 0; p < ChaikinPasses; p++)
                Chaikin(closed);
            Simplify(closed);
            EmitBeziers(closed, keyLo, keyHi);
        }

        private void Chaikin(bool closed)
        {
            int n = m_Pts.Count;
            if (n < 3)
                return;
            m_Tmp.Clear();
            if (!closed)
                m_Tmp.Add(m_Pts[0]);
            int segs = closed ? n : n - 1;
            for (int k = 0; k < segs; k++)
            {
                double2 a = m_Pts[k], b = m_Pts[(k + 1) % n];
                m_Tmp.Add(0.75 * a + 0.25 * b);
                m_Tmp.Add(0.25 * a + 0.75 * b);
            }
            if (!closed)
            {
                m_Tmp.RemoveAt(1);                 // keep the exact end points: drop the first Q and the last R next to them
                m_Tmp.RemoveAt(m_Tmp.Count - 1);
                m_Tmp.Add(m_Pts[n - 1]);
            }
            m_Pts.Clear();
            m_Pts.AddRange(m_Tmp);
        }

        private void Simplify(bool closed)
        {
            int n = m_Pts.Count;
            if (n < 3 || !(SimplifyM > 0f))
                return;
            if (m_Keep.Length < n)
                m_Keep = new bool[Math.Max(n, 2 * m_Keep.Length)];
            Array.Clear(m_Keep, 0, n);
            m_Keep[0] = m_Keep[n - 1] = true;
            if (closed)
                m_Keep[n / 2] = true;               // a closed ring: split it so each half has distinct ends
            m_Dp.Clear();
            if (closed)
            {
                m_Dp.Push(new int2(0, n / 2));
                m_Dp.Push(new int2(n / 2, n - 1));
            }
            else
                m_Dp.Push(new int2(0, n - 1));
            double tol2 = (double)SimplifyM * SimplifyM;
            while (m_Dp.Count > 0)
            {
                int2 r = m_Dp.Pop();
                if (r.y - r.x < 2)
                    continue;
                double2 a = m_Pts[r.x], b = m_Pts[r.y];
                int best = -1;
                double bestD = tol2;
                for (int k = r.x + 1; k < r.y; k++)
                {
                    double d = SegDist2(m_Pts[k], a, b);
                    if (d > bestD)
                    {
                        bestD = d;
                        best = k;
                    }
                }
                if (best < 0)
                    continue;
                m_Keep[best] = true;
                m_Dp.Push(new int2(r.x, best));
                m_Dp.Push(new int2(best, r.y));
            }
            m_Simple.Clear();
            for (int k = 0; k < n; k++)
                if (m_Keep[k])
                    m_Simple.Add(m_Pts[k]);
            m_Pts.Clear();
            m_Pts.AddRange(m_Simple);
        }

        private static double SegDist2(double2 p, double2 a, double2 b)
        {
            double2 ab = b - a;
            double l2 = math.dot(ab, ab);
            double t = l2 > 0 ? math.clamp(math.dot(p - a, ab) / l2, 0.0, 1.0) : 0.0;
            double2 d = p - (a + t * ab);
            return math.dot(d, d);
        }

        /// <summary>Bezier controls of the centripetal Catmull-Rom span p1 -> p2 (alpha 0.5). The uniform form makes cusps and loops where
        /// the point spacing changes much from one span to the next (after the simplify); the centripetal form does not.</summary>
        private static void CentripetalControls(double2 p0, double2 p1, double2 p2, double2 p3, out double2 c1, out double2 c2)
        {
            double l1 = math.distance(p0, p1), l2 = math.distance(p1, p2), l3 = math.distance(p2, p3);
            double d1 = Math.Sqrt(l1), d2 = Math.Sqrt(l2), d3 = Math.Sqrt(l3);
            c1 = l1 > 1e-9 && l2 > 1e-9
                ? (l1 * p2 - l2 * p0 + (2 * l1 + 3 * d1 * d2 + l2) * p1) / (3 * d1 * (d1 + d2))
                : p1 + (p2 - p1) / 3.0;
            c2 = l3 > 1e-9 && l2 > 1e-9
                ? (l3 * p1 - l2 * p3 + (2 * l3 + 3 * d3 * d2 + l2) * p2) / (3 * d3 * (d3 + d2))
                : p2 + (p1 - p2) / 3.0;
        }

        private void EmitBeziers(bool closed, byte keyLo, byte keyHi)
        {
            int n = m_Pts.Count;
            if (closed && n >= 2 && math.distance(m_Pts[0], m_Pts[n - 1]) < 1e-6)
                n--;                                 // a ring stored with a duplicate end
            if (n < 2)
                return;
            int segs = closed ? n : n - 1;
            for (int k = 0; k < segs; k++)
            {
                double2 p1 = m_Pts[k], p2 = m_Pts[(k + 1) % n];
                double2 p0 = closed ? m_Pts[(k - 1 + n) % n] : (k > 0 ? m_Pts[k - 1] : p1);
                double2 p3 = closed ? m_Pts[(k + 2) % n] : (k + 2 < n ? m_Pts[k + 2] : p2);
                CentripetalControls(p0, p1, p2, p3, out double2 c1, out double2 c2);
                double edgeM = double.PositiveInfinity;
                if (Disk)
                    edgeM = Math.Max(0.0, DiskR - math.length(0.5 * (p1 + p2) - new double2(DiskCx, DiskCz)));
                var cv = new Curve { KeyLo = keyLo, KeyHi = keyHi, Fade = FadeLevels };
                cv.P0 = P3(p1);
                cv.P1 = P3(c1);
                cv.P2 = P3(c2);
                cv.P3 = P3(p2);
                if (m_H != null)
                {
                    double x0 = Math.Min(Math.Min(p1.x, p2.x), Math.Min(c1.x, c2.x)), x1 = Math.Max(Math.Max(p1.x, p2.x), Math.Max(c1.x, c2.x));
                    double z0 = Math.Min(Math.Min(p1.y, p2.y), Math.Min(c1.y, c2.y)), z1 = Math.Max(Math.Max(p1.y, p2.y), Math.Max(c1.y, c2.y));
                    Box(x0 - m_Step, z0 - m_Step, x1 + m_Step, z1 + m_Step, out cv.YLo, out cv.YHi);
                }
                else
                {
                    cv.YLo = 1f;
                    cv.YHi = 0f;
                }
                if (Disk)
                    cv.Fade = FadeOf(edgeM);
                Curves.Add(cv);
            }
        }

        private float3 P3(double2 p) => new float3((float)p.x, m_H != null ? HeightAt(p.x, p.y) : 0f, (float)p.y);

        // ---- heights ----
        /// <summary>Bilinear node height at world (x, z); NaN-aware (falls back to the valid corners, 0 when none).</summary>
        public float HeightAt(double x, double z)
        {
            if (m_H == null)
                return 0f;
            double fx = (x - m_Ox) / m_Step, fz = (z - m_Oz) / m_Step;
            int i = (int)Math.Floor(fx), j = (int)Math.Floor(fz);
            double tx = fx - i, tz = fz - j;
            double num = 0, den = 0;
            for (int dj = 0; dj <= 1; dj++)
                for (int di = 0; di <= 1; di++)
                {
                    int ii = Math.Max(0, Math.Min(m_Nx - 1, i + di)), jj = Math.Max(0, Math.Min(m_Nz - 1, j + dj));
                    float v = m_H[jj * m_Nx + ii];
                    if (float.IsNaN(v))
                        continue;
                    double w = (di == 0 ? 1 - tx : tx) * (dj == 0 ? 1 - tz : tz) + 1e-9;
                    num += w * v;
                    den += w;
                }
            return den > 0 ? (float)(num / den) : 0f;
        }

        /// <summary>Lowest and highest node height in the world box, plus BoxMarginM. YHi < YLo when no height is known.</summary>
        private void Box(double x0, double z0, double x1, double z1, out float lo, out float hi)
        {
            int i0 = Math.Max(0, (int)Math.Floor((x0 - m_Ox) / m_Step)), i1 = Math.Min(m_Nx - 1, (int)Math.Ceiling((x1 - m_Ox) / m_Step));
            int j0 = Math.Max(0, (int)Math.Floor((z0 - m_Oz) / m_Step)), j1 = Math.Min(m_Nz - 1, (int)Math.Ceiling((z1 - m_Oz) / m_Step));
            float mn = float.MaxValue, mx = float.MinValue;
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    float v = m_H[j * m_Nx + i];
                    if (float.IsNaN(v))
                        continue;
                    mn = Math.Min(mn, v);
                    mx = Math.Max(mx, v);
                }
            if (mn > mx)
            {
                lo = 1f;
                hi = 0f;
                return;
            }
            lo = mn - BoxMarginM;
            hi = mx + BoxMarginM;
        }
    }
}
