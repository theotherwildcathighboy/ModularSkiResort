// Read-only debug overlay of the tower planner. While a lift line is the selected entity (ToolSystem.selected, ToolSystem.cs:101:
// a section edge, a node, or an object it owns, e.g. a tower) it draws the line's planned towers and rope as LiftTowerDiagSystem
// planned them on the real terrain. Rope colored by the lowest carrier point's floor margin: green >= 2 m, amber 0-2 m, red < 0,
// violet = above the max rope height. Masts are not drawn: OverlayRenderSystem.Buffer.DrawLine measures a line by its xz length
// (OverlayRenderSystem.cs:209) and DrawCurveImpl drops anything under 0.01 m (:269), so a vertical line never renders.
// Changes nothing; gated by Settings > Lifts > LiftBuildAids. Buffer use is the main-thread pattern of SkiStationAlignSystem:
// GetBuffer(out deps), deps.Complete() (OverlayRenderSystem.cs:603). Logs one "Towers [overlay]" line per newly selected line.
// Mod.cs: UpdateBefore<LiftTowerPlanOverlaySystem, OverlayRenderSystem>(Rendering): the renderer clears the lists later (:781, :790).

using System;
using System.Collections.Generic;
using Colossal.Entities;
using Colossal.Mathematics;
using Game;
using Game.Common;
using Game.Rendering;
using Game.Tools;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>World-space geometry of one planned line (built once per plan by LiftTowerDiagSystem).</summary>
    internal sealed class LiftPlanDraw
    {
        public string TypeId;
        public float3[] Base; // tower base on the center ground (stations: the end node point at ground)
        public float3[] Rope;
        public float3[] Right; // unit right of the line at the node
        public bool[] IsStation;
        public int[] Member;
        public int LadderLength;
        public float3[] SegA, SegB;
        public byte[] SegClass; // 0 green, 1 amber, 2 red (floor), 3 violet (max height)
        public int Towers;
        public readonly int[] ClassCounts = new int[4];

        public static LiftPlanDraw Build(LiftTowerPlan plan, Func<float, float3> xzAt, Func<float, float3> rightAt)
        {
            int n = plan.Nodes.Length;
            var d = new LiftPlanDraw
            {
                TypeId = plan.Constants.TypeId,
                Base = new float3[n], Rope = new float3[n], Right = new float3[n], IsStation = new bool[n], Member = new int[n],
                LadderLength = plan.Constants.Ladder.Length, Towers = plan.LineTowers,
            };
            for (int i = 0; i < n; i++)
            {
                LiftPlanNode node = plan.Nodes[i];
                float3 p = xzAt(node.AlongM);
                d.Base[i] = new float3(p.x, node.GroundM, p.z);
                d.Rope[i] = new float3(p.x, node.RopeM, p.z);
                d.Right[i] = rightAt(node.AlongM);
                d.IsStation[i] = node.IsStation;
                d.Member[i] = node.Member;
            }
            var a = new List<float3>();
            var b = new List<float3>();
            var cls = new List<byte>();
            float cap = plan.Constants.MaxRopeAboveGroundM;
            float[] s = plan.SampleAlongM;
            for (int span = 0; span < plan.Spans.Length; span++)
            {
                float from = plan.Nodes[span].AlongM, to = plan.Nodes[span + 1].AlongM;
                float prevAlong = from;
                float prevMargin = float.NaN, prevAbove = float.NaN;
                for (int q = 0; q < s.Length; q++)
                {
                    if (s[q] < from - 1e-3f || s[q] > to + 1e-3f)
                        continue;
                    float margin = plan.MarginAt(span, q);
                    float above = plan.RopeAt(span, s[q]) - plan.SampleCentreM[q];
                    if (!float.IsNaN(prevMargin) && s[q] - prevAlong > 1e-3f)
                    {
                        float3 pa = xzAt(prevAlong), pb = xzAt(s[q]);
                        a.Add(new float3(pa.x, plan.RopeAt(span, prevAlong), pa.z));
                        b.Add(new float3(pb.x, plan.RopeAt(span, s[q]), pb.z));
                        float m = math.min(margin, prevMargin);
                        byte c = cap > 0f && math.max(above, prevAbove) > cap + 0.01f ? (byte)3 : m < -0.01f ? (byte)2 : m < 2f ? (byte)1 : (byte)0;
                        cls.Add(c);
                        d.ClassCounts[c]++;
                    }
                    prevAlong = s[q];
                    prevMargin = margin;
                    prevAbove = above;
                }
            }
            d.SegA = a.ToArray();
            d.SegB = b.ToArray();
            d.SegClass = cls.ToArray();
            return d;
        }
    }

    public sealed partial class LiftTowerPlanOverlaySystem : GameSystemBase
    {
        private const float RopeWidthM = 0.6f;
        private const float BaseRingM = 3f;
        private const float RopeRingM = 1.6f;
        private static readonly UnityEngine.Color[] SegColors =
        {
            new UnityEngine.Color(0.25f, 0.85f, 0.35f, 0.9f),
            new UnityEngine.Color(1.0f, 0.75f, 0.15f, 0.9f),
            new UnityEngine.Color(0.95f, 0.2f, 0.2f, 0.95f),
            new UnityEngine.Color(0.7f, 0.35f, 1.0f, 0.95f),
        };
        private static readonly UnityEngine.Color TowerColor = new UnityEngine.Color(1f, 1f, 1f, 0.9f);
        private static readonly UnityEngine.Color StationColor = new UnityEngine.Color(0.55f, 0.8f, 1f, 0.9f);

        private ToolSystem m_ToolSystem;
        private OverlayRenderSystem m_Overlay;
        private LiftTowerDiagSystem m_Diag;
        private Entity m_LastSelected;
        private LiftPlanDraw m_LastDraw;
        private bool m_WaitLogged;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_Overlay = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
            m_Diag = World.GetOrCreateSystemManaged<LiftTowerDiagSystem>();
            Mod.Log.Info($"{nameof(LiftTowerPlanOverlaySystem)} created");
        }

        protected override void OnUpdate()
        {
            if (Mod.Settings != null && !Mod.Settings.LiftBuildAids)
                return;
            // Never under the lift tool, whose editable preview uses the same rings: two look-alike overlays would suggest the placed
            // line can be edited there.
            if (m_ToolSystem.activeTool is LiftLineToolSystem)
                return;
            Entity selected = m_ToolSystem.selected;
            if (selected == Entity.Null)
            {
                m_LastSelected = Entity.Null;
                m_LastDraw = null;
                return;
            }
            if (selected != m_LastSelected)
            {
                m_LastSelected = selected;
                m_LastDraw = null;
                m_WaitLogged = false;
            }
            if (!TryResolve(selected, out LiftPlanDraw draw, out Entity key))
            {
                m_Diag.RequestPlan(key);
                return;
            }
            if (!ReferenceEquals(draw, m_LastDraw))
            {
                m_LastDraw = draw;
                Mod.Log.Info($"Towers [overlay]: drawing the planned {draw.TypeId} line for the selected {selected.Index}:{selected.Version} — " +
                             $"{draw.Towers} planned line tower(s); rope samples green {draw.ClassCounts[0]}, amber {draw.ClassCounts[1]}, " +
                             $"red {draw.ClassCounts[2]}, violet {draw.ClassCounts[3]} (see its Towers [plan] block)");
            }
            Draw(draw);
        }

        /// <summary>The plan of the selected entity, or of its Owner (a tower's owner is its node). <paramref name="key"/> is the
        /// entity to ask the diag system for when nothing is cached.</summary>
        private bool TryResolve(Entity selected, out LiftPlanDraw draw, out Entity key)
        {
            key = selected;
            if (m_Diag.TryGetPlanDraw(selected, out draw))
                return true;
            if (EntityManager.TryGetComponent<Owner>(selected, out var owner) && owner.m_Owner != Entity.Null)
            {
                key = owner.m_Owner;
                if (m_Diag.TryGetPlanDraw(owner.m_Owner, out draw))
                    return true;
            }
            if (!m_WaitLogged && (EntityManager.HasComponent<Game.Net.Edge>(key) || EntityManager.HasComponent<Game.Net.Node>(key)))
            {
                m_WaitLogged = true;
                Mod.Log.Info($"Towers [overlay]: selected {selected.Index}:{selected.Version} -> net {key.Index}:{key.Version} has no cached plan " +
                             "— asked the tower diagnostic to plan it (a lift line gets a Towers [plan] block next update; other nets are ignored)");
            }
            return false;
        }

        private void Draw(LiftPlanDraw d)
        {
            // Main-thread write: complete the existing writers first (OverlayRenderSystem.cs:603-634)
            OverlayRenderSystem.Buffer buffer = m_Overlay.GetBuffer(out JobHandle deps);
            deps.Complete();
            var absolute = (OverlayRenderSystem.StyleFlags)0;
            var round = new float2(1f, 1f);
            for (int i = 0; i < d.SegA.Length; i++)
            {
                UnityEngine.Color c = SegColors[d.SegClass[i]];
                buffer.DrawLine(c, c, 0f, absolute, new Line3.Segment(d.SegA[i], d.SegB[i]), RopeWidthM, round); // OverlayRenderSystem.cs:207
            }
            for (int i = 0; i < d.Base.Length; i++)
            {
                if (d.IsStation[i])
                {
                    buffer.DrawCircle(StationColor, StationColor, 0f, absolute, new float2(0f, 1f), d.Rope[i], RopeRingM); // OverlayRenderSystem.cs:138
                    continue;
                }
                buffer.DrawCircle(TowerColor, TowerColor, 0f, OverlayRenderSystem.StyleFlags.Projected, new float2(0f, 1f), d.Base[i], BaseRingM);
                buffer.DrawCircle(TowerColor, TowerColor, 0f, absolute, new float2(0f, 1f), d.Rope[i], RopeRingM);
                float half = 2f + 2f * d.Member[i] / math.max(1, d.LadderLength - 1); // 2 m (shortest) .. 4 m (tallest)
                float3 r = d.Right[i] * half;
                buffer.DrawLine(TowerColor, TowerColor, 0f, absolute, new Line3.Segment(d.Rope[i] - r, d.Rope[i] + r), 0.4f, round);
            }
        }
    }
}
