// Runs-first realizer: lifts (spec section 7). Each planner lift becomes a PlanLift row: the catalog type by name, both ends at snapshot
// heights, the tower plan (the lift tool's planner, called as v2 calls it), the cost, a key, its own group and pads. A lift whose
// towers do not clear stays in the plan, flagged TowersUnchecked: the planner's network was built with it. Pure.
// Spec: docs/research/quickruns-v2/port/REALIZER-SPEC.md section 7.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class RunsFirstRealizer
    {
        /// <summary>The ground along a lift chord for LiftTowerPlanner (copied from SkiRunAutoPlanner.Lifts ChordSampler). A miss returns the
        /// last good height and is counted: a plan with a miss is not clear.</summary>
        private sealed class ChordSampler
        {
            public HeightSnapshot Snap;
            public float2 A, Dir, Right;
            public float Last;
            public int Misses;

            public float Sample(float along, float lateral)
            {
                float2 p = A + Dir * along + Right * lateral;
                float h = Snap.Sample(new float3(p.x, 0f, p.y));
                if (float.IsNaN(h))
                {
                    Misses++;
                    return Last;
                }
                Last = h;
                return h;
            }
        }

        /// <summary>The catalog index of a planner lift type: fixed-grip 0, detachable 1, gondola 2, tram 3, "beginner" = fixed grip.</summary>
        public static byte TypeIndexOf(string type)
        {
            switch (type)
            {
                case "detachable": return 1;
                case "gondola": return 2;
                case "tram": return 3;
                default: return 0;
            }
        }

        private void RealizeLifts()
        {
            List<RunsFirstResult.Lift> list = m_Plan.Lifts;
            m_Lifts = new PlanLift[list.Count];
            m_Rep.Lifts = list.Count;
            var planner = new LiftTowerPlanner();
            var consts = new LiftTowerPlanConstants[PlanGeometry.StationTypes];
            var cs = new ChordSampler { Snap = m_Snap };
            for (int i = 0; i < list.Count; i++)
            {
                RunsFirstResult.Lift L = list[i];
                byte t = TypeIndexOf(L.Type);
                if (consts[t] == null)
                    consts[t] = LiftTowerPlanConstants.For(SkiCatalog.Lifts[t]);
                float3 bottom = Ground(L.Bottom), top = Ground(L.Top);
                float len = math.distance(bottom.xz, top.xz);
                LiftTowerPlan plan = null;
                bool clear = false;
                if (len > 1f)
                    plan = TowerPlan(planner, consts[t], cs, t, bottom, top, len, out clear);
                var towers = new float2[plan?.LineTowers ?? 0];
                float2 dir = len > 1f ? (top.xz - bottom.xz) / len : new float2(1f, 0f);
                for (int n = 1, w = 0; plan != null && n < plan.Nodes.Length - 1 && w < towers.Length; n++)
                    if (!plan.Nodes[n].IsStation)
                        towers[w++] = bottom.xz + dir * plan.Nodes[n].AlongM;
                LiftTypeDef def = SkiCatalog.Lifts[t];
                long cost = def.StationCostBase + def.StationCostTop + (long)Math.Round(def.LinePerKm * (double)len / 1000.0);
                uint key = 2166136261u;
                key = PlanGeometry.Fnv(key, t);
                key = PlanGeometry.Fnv(key, (uint)(int)Math.Round(bottom.x));
                key = PlanGeometry.Fnv(key, (uint)(int)Math.Round(bottom.z));
                key = PlanGeometry.Fnv(key, (uint)(int)Math.Round(top.x));
                key = PlanGeometry.Fnv(key, (uint)(int)Math.Round(top.z));
                bool bSunk = Sunk(bottom.xz), tSunk = Sunk(top.xz);
                m_Lifts[i] = new PlanLift
                {
                    Id = i, Key = key, Origin = LiftOrigin.Proposed, TypeIndex = t, Bottom = bottom, Top = top, LengthM = len, RiseM = top.y - bottom.y,
                    MaxEdgeSlope = plan?.MaxEdgeSlope ?? 0f, Towers = plan?.LineTowers ?? -1, TowerXz = towers, TowersUnchecked = !clear,
                    Pod = i, BottomPad = 2 * i, TopPad = 2 * i + 1, TopSunk = tSunk, BottomSunk = bSunk, Cost = cost, Home = HomeVerdict.None,
                    Wanted = HomeLevel.Auto, HomeRun = -1,
                };
                if (clear)
                    m_Rep.LiftsClear++;
                else
                    m_Rep.Lines.Add($"lift L{L.Id} ({L.Type} {len:F0} m / {top.y - bottom.y:F0} m up): towers do not clear{(cs.Misses > 0 ? " (a sampler miss)" : "")}, kept");
                if (bSunk || tSunk)
                {
                    m_Rep.SunkEnds += (bSunk ? 1 : 0) + (tSunk ? 1 : 0);
                    m_Rep.Lines.Add($"lift L{L.Id}: {(bSunk ? "bottom" : "")}{(bSunk && tSunk ? " and " : "")}{(tSunk ? "top" : "")} station on ground over {SunkGrade:P0}");
                }
            }
            // Lots: two that overlap, or one on a blocked cell, are reported (never moved)
            for (int i = 0; i < m_Lifts.Length; i++)
                for (int s = 0; s < 2; s++)
                {
                    float2 p = s == 0 ? m_Lifts[i].Bottom.xz : m_Lifts[i].Top.xz;
                    float r = PlanGeometry.StationHalfDiagonalOf(m_Lifts[i].TypeIndex);
                    for (int k = i + 1; k < m_Lifts.Length; k++)
                        for (int q = 0; q < 2; q++)
                        {
                            float2 o = q == 0 ? m_Lifts[k].Bottom.xz : m_Lifts[k].Top.xz;
                            if (math.distance(p, o) < r + PlanGeometry.StationHalfDiagonalOf(m_Lifts[k].TypeIndex))
                            {
                                m_Rep.LotConflicts++;
                                m_Rep.Lines.Add($"lots overlap: L{i + 1} {(s == 0 ? "bottom" : "top")} and L{k + 1} {(q == 0 ? "bottom" : "top")} ({math.distance(p, o):F0} m apart)");
                            }
                        }
                    if (DiscBlocked(p, r))
                    {
                        m_Rep.LotConflicts++;
                        m_Rep.Lines.Add($"lot on a blocked cell: L{i + 1} {(s == 0 ? "bottom" : "top")} at ({p.x:F0}, {p.y:F0})");
                    }
                }
        }

        private float3 Ground(float3 p)
        {
            float h = H(p.xz);
            return new float3(p.x, float.IsNaN(h) ? p.y : h, p.z);
        }

        private bool Sunk(float2 p) => Cell(p, out int c) && m_Grade[c] > SunkGrade;

        private bool DiscBlocked(float2 p, float r)
        {
            int k = (int)math.ceil(r / m_Texel);
            if (!Cell(p, out int c0))
                return true;
            int ci = c0 % m_Nx, cj = c0 / m_Nx;
            for (int j = Math.Max(0, cj - k); j <= Math.Min(m_Nz - 1, cj + k); j++)
                for (int i = Math.Max(0, ci - k); i <= Math.Min(m_Nx - 1, ci + k); i++)
                    if (math.distance(CellCenter(i, j), p) <= r && (m_Flags[j * m_Nx + i] & FBlockAll) != 0)
                        return true;
            return false;
        }

        /// <summary>The lift tool's planner on the chord (bottom = station A), as SkiRunAutoPlanner.Lifts TowerPlan calls it (copied):
        /// stations at both ends, the engine's edge slope rule with the end nodes at the snapshot heights, ropes at the station's own
        /// anchor. Clear = no sampler miss, every span clear, edge slopes under LiftEdgeSlopeShare x the type cap.</summary>
        private static LiftTowerPlan TowerPlan(LiftTowerPlanner planner, LiftTowerPlanConstants k, ChordSampler s, byte type, float3 bottom,
            float3 top, float len, out bool clear)
        {
            s.A = bottom.xz;
            s.Dir = (top.xz - bottom.xz) / len;
            s.Right = new float2(s.Dir.y, -s.Dir.x);
            s.Last = bottom.y;
            s.Misses = 0;
            planner.LegacySpacing = false;
            planner.Forbidden = null;
            planner.StationA = planner.StationB = true;
            planner.StationRule = true;
            planner.CheckEdgeSlope = true;
            planner.EndNodeYA = bottom.y;
            planner.EndNodeYB = top.y;
            float ropeA = k.StationEndRope(SkiCatalog.StationOf(type, StationRole.Base), bottom.y);
            float ropeB = k.StationEndRope(SkiCatalog.StationOf(type, StationRole.Top), top.y);
            LiftTowerPlan plan = planner.Plan(s.Sample, len, ropeA, ropeB, k);
            clear = s.Misses == 0 && plan != null && plan.Clear && plan.MaxEdgeSlope <= LiftEdgeSlopeShare * k.MaxEdgeSlope;
            return plan;
        }
    }
}
