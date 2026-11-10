// Runs-first realizer: lifts (spec section 7). Each planner lift becomes a PlanLift row: the catalog type by name, both ends at snapshot
// heights, the tower plan (the line the build queue plans between the stations it places: LiftLineInput.PlanQueueLine, task 2b-l), the
// cost, a key, its own group and pads. A lift whose towers do not clear stays in the plan, flagged TowersUnchecked: the planner's network
// was built with it. Pure.
// Spec: docs/research/quickruns-v2/port/REALIZER-SPEC.md section 7.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class RunsFirstRealizer
    {
        /// <summary>The snapshot ground at a world point for LiftLineInput.PlanQueueLine. A miss returns the last good height and is
        /// counted: a plan with a miss is not clear.</summary>
        private sealed class QueueGround
        {
            public HeightSnapshot Snap;
            public float Last;
            public int Misses;

            public float Sample(float3 p)
            {
                float h = Snap.Sample(new float3(p.x, 0f, p.z));
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
            var cs = new QueueGround { Snap = m_Snap };
            Func<float3, float> ground = cs.Sample;
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
                float2 tipA = bottom.xz, dir = len > 1f ? (top.xz - bottom.xz) / len : new float2(1f, 0f);
                cs.Misses = 0;
                cs.Last = bottom.y;
                if (len > 1f)
                {
                    // Task 2b-l: the line the queue plans between the stations it places (the shared station height rule)
                    plan = LiftLineInput.PlanQueueLine(consts[t], SkiCatalog.StationOf(t, StationRole.Base), SkiCatalog.StationOf(t, StationRole.Top),
                        bottom.xz, top.xz, ground, planner, out StationAlign.QueueStation qb, out _, out LiftLineEdit edit);
                    clear = cs.Misses == 0 && plan != null && plan.Clear && plan.MaxEdgeSlope <= LiftEdgeSlopeShare * consts[t].MaxEdgeSlope;
                    if (edit != null)
                    {
                        tipA = qb.Tip.xz;
                        dir = edit.LengthM > 1e-3f ? (edit.B.xz - edit.A.xz) / edit.LengthM : dir;
                    }
                }
                // Iteration 3, item 5: clear means Sound in the lift tool's sense (LiftLineToolSystem.AutoBuildBlocker: every span clear, no
                // tower over its sheave-train cap, none too steep, none past its carrier clearance). Else the lift waits for a hand fit.
                bool edgeClear = clear;
                string why = cs.Misses > 0 ? "a sampler miss" : plan == null ? "no tower plan" : !plan.Clear ? "a span not clear"
                    : plan.OverCapTowers > 0 ? $"{plan.OverCapTowers} tower(s) over the cap" : plan.TooSteepTowers > 0 ? $"{plan.TooSteepTowers} tower(s) too steep"
                    : plan.ClearanceTowers > 0 ? $"{plan.ClearanceTowers} tower(s) past the clearance" : !clear ? "an edge slope"
                    : plan.LeansOnStationClearance ? "a tower inside the station clearance" : null;
                clear = why == null;
                // Task R2-l: the queue still builds a line with exactly one tower over its sheave-train limit and no other defect, and flags it.
                bool flagged = !clear && cs.Misses == 0 && plan != null && edgeClear && plan.PlanBuild == LiftQueueBuild.BuildFlagged;
                // Task 2c-l: the row's reason before the build, in the queue's words (LiftLineInput.PlanKey; a plan leaning on the station
                // clearance: PLAN_TRAIN, what the queue met on those lines) and its first over-limit tower
                string planWhy = clear ? null : cs.Misses > 0 || plan == null ? "GAME" : plan.Clear && plan.OverCapTowers == 0 && plan.LeansOnStationClearance
                    ? "PLAN_TRAIN" : LiftLineInput.PlanKey(plan, out _);
                if (planWhy == "PLAN_CLEAR")
                    planWhy = "PLAN_STEEP";   // the edge slope margin: the line prefab's slope
                int ot = plan != null && plan.OverLimitTowers > 0 ? plan.FirstOverCapTower() : -1;
                var towers = new float2[plan?.LineTowers ?? 0];
                for (int n = 1, w = 0; plan != null && n < plan.Nodes.Length - 1 && w < towers.Length; n++)
                    if (!plan.Nodes[n].IsStation)
                        towers[w++] = tipA + dir * plan.Nodes[n].AlongM;   // along from the base stub tip
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
                    MaxEdgeSlope = plan?.MaxEdgeSlope ?? 0f, Towers = plan?.LineTowers ?? -1, TowerXz = towers, TowersUnchecked = !clear, HandFit = !clear,
                    OverLimitTowers = plan?.OverLimitTowers ?? 0, BuildsFlagged = flagged, PlanWhy = planWhy, OverLimitTower = ot,
                    OverLimitAlongM = ot >= 0 ? plan.Nodes[ot].AlongM : 0f, OverLimitBendDeg = ot >= 0 ? math.abs(plan.Nodes[ot].DeflectionDeg) : 0f,
                    OverLimitCapDeg = ot >= 0 ? plan.Nodes[ot].CapDeg : 0f,
                    Pod = i, BottomPad = 2 * i, TopPad = 2 * i + 1, TopSunk = tSunk, BottomSunk = bSunk, Cost = cost, Home = HomeVerdict.None,
                    Wanted = HomeLevel.Auto, HomeRun = -1,
                };
                if (clear)
                    m_Rep.LiftsClear++;
                else
                {
                    m_Rep.HandFitByType[Math.Min(3, (int)t)]++;
                    if (flagged)
                        m_Rep.LiftsFlagged++;
                    if (cs.Misses > 0 || plan == null)
                        m_Rep.HandFitMiss++;
                    else if (!plan.Clear)
                        m_Rep.HandFitSpans++;
                    else if (plan.OverCapTowers > 0)
                        m_Rep.HandFitOverCap++;
                    else if (plan.TooSteepTowers > 0)
                        m_Rep.HandFitSteep++;
                    else if (plan.ClearanceTowers > 0)
                        m_Rep.HandFitClearance++;
                    else if (edgeClear && plan.LeansOnStationClearance)
                        m_Rep.HandFitNear++;
                    else
                        m_Rep.HandFitEdge++;
                    m_Rep.Lines.Add($"lift L{L.Id} ({L.Type} {len:F0} m / {top.y - bottom.y:F0} m up): tower plan not sound ({why}), kept, waits for a hand fit");
                }
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
    }
}
