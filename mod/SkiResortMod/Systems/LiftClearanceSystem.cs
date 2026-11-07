// The hub's low-span warning (LiftLowSpan, 2026-11-03). It measures how high the carriers pass over the ground between two towers, on the
// line as it stands. The planner keeps carriers FloorM above the ground (LiftTowerPlanConstants: 3 m, the tram 5 m). A line can stand lower
// than that later. Two cases: a line fitted at a deeper rope sag than it was planned for (LiftTowerFitSystem raises and slides towers,
// never adds one, DECISIONS 587 d2), or terrain raised under a finished line.
// This pass measures the real rope. It samples every HV cable lane of a lift edge (the lanes the carriers ride, ChairPinSystem.TryPinChair)
// every StepM. The Curve of a lane already carries its sag. The carrier's lowest point is the rope point minus the type's CarrierDropM
// (catalog HangM). The ground is the terrain under it. Samples within StationClearM of a station end are skipped (the carriers come down to
// loading height there, and the planner's floor ramps to 0 the same way, LiftTowerPlanConstants.ApproachFloor). An edge whose lowest
// carrier passes more than TolM under the floor enters Low. When that set changes, the pass asks LiftCensusSystem for a coalesced build.
// The build reads the set into LiftCensus.Edge.Low*.
// When: after each census build (its edge list), when a lift edge is Updated (a tower moved: its lanes are rebuilt this frame), and every
// RemeasureFrames (terrain raised under a line updates no lift entity). A pass measures whole edges until SliceSamples terrain samples per
// frame. Field test 2026-11-03: about 0.9 us a sample, 62 rope lanes / 1,682 samples took 1.2 ms in one frame, 16 edges a frame still 1.34
// ms. So a big resort never stalls one frame. A new trigger mid-pass starts it over.
// Never: Temp or Deleted edges and lanes, the Editor. Warn only: the line, its towers and the lift state are unchanged.
// Log discipline: one line when the set changes (with the worst span), a one-shot "!!!" catch.
// Mod.cs: UpdateAfter<LiftClearanceSystem, LiftCensusSystem>(Modification4B).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Colossal.Entities;
using Colossal.Mathematics;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using SubLane = Game.Net.SubLane;

namespace SkiResortMod.Systems
{
    public sealed partial class LiftClearanceSystem : GameSystemBase
    {
        /// <summary>A span warns when its lowest carrier passes more than this under the floor (the planner's own tolerance is 0.05 m.
        /// The live rope and the terrain under it are read, not planned).</summary>
        internal const float TolM = 0.1f;

        /// <summary>Re-measure period, frames (about 10 s at 60 fps): terrain raised under a line updates no lift entity.</summary>
        internal const int RemeasureFrames = 600;

        /// <summary>Terrain samples per frame, about 0.25 ms. Whole edges are measured until a frame reaches it. A chair edge is 40 to 80
        /// samples over its two rope lanes. A long tram span is up to 2 x 300, and it then has a frame of its own.</summary>
        internal const int SliceSamples = 256;

        /// <summary>At most this many samples per lane (a 3 km tram span at the tram's 10 m step is 300).</summary>
        internal const int MaxSamples = 400;

        /// <summary>The lowest carrier of a low span: meters above the ground (negative: below it) and where.</summary>
        internal struct Low
        {
            public float ClearM;
            public float3 At;
        }

        private TerrainSystem m_TerrainSystem;
        private LiftCensusSystem m_Census;
        private EntityQuery m_UpdatedEdges;
        private bool m_Armed, m_Disabled, m_Dirty;
        private string m_DirtyWhy = "load";
        private int m_Frame, m_LastPassFrame, m_Passes;
        // The pass in progress: m_Cursor = the next edge (-1 = none), its trigger, counters and time (Stopwatch ticks, all frames / the
        // largest one).
        private int m_Cursor = -1, m_PassLanes, m_PassSamples, m_PassFrames;
        private long m_PassTicks, m_PassMaxTicks;
        private string m_PassWhy;
        private readonly List<LiftCensus.Edge> m_Edges = new List<LiftCensus.Edge>();
        private readonly Dictionary<byte, LiftTowerPlanConstants> m_Constants = new Dictionary<byte, LiftTowerPlanConstants>();
        private Dictionary<Entity, Low> m_Low = new Dictionary<Entity, Low>();
        private Dictionary<Entity, Low> m_LowNext = new Dictionary<Entity, Low>();

        /// <summary>The lift edges whose lowest carrier passes under the floor (last pass).</summary>
        internal IReadOnlyDictionary<Entity, Low> LowEdges => m_Low;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_TerrainSystem = World.GetOrCreateSystemManaged<TerrainSystem>();
            m_Census = World.GetOrCreateSystemManaged<LiftCensusSystem>();
            m_UpdatedEdges = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Updated>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
            Mod.Log.Info($"{nameof(LiftClearanceSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Armed = mode == GameMode.Game;
            m_Disabled = false;
            m_Dirty = false;
            m_Edges.Clear();
            m_Low.Clear();
            m_Passes = 0;
            m_Cursor = -1;
            m_Frame = 0;
            m_LastPassFrame = 0;
        }

        /// <summary>Called by LiftCensusSystem after each build with its line edges (main thread, no ECS read here).</summary>
        internal void OnCensusBuilt(List<LiftCensus.Edge> edges)
        {
            if (!m_Armed || m_Disabled)
                return;
            m_Edges.Clear();
            m_Edges.AddRange(edges);
            Arm("census");
        }

        private void Arm(string why)
        {
            if (!m_Dirty)
                m_DirtyWhy = why;
            m_Dirty = true;
        }

        protected override void OnUpdate()
        {
            if (!m_Armed || m_Disabled)
                return;
            m_Frame++;
            try
            {
                if (!m_Dirty && m_Edges.Count > 0 && !m_UpdatedEdges.IsEmptyIgnoreFilter && LiftEdgeUpdated())
                    Arm("edge updated");
                if (!m_Dirty && m_Cursor < 0 && m_Edges.Count > 0 && m_Frame - m_LastPassFrame >= RemeasureFrames)
                    Arm("remeasure");
                if (!m_Dirty && m_Cursor < 0)
                    return;
                TerrainHeightData hd = m_TerrainSystem.GetHeightData();
                if (!hd.isCreated)
                    return;   // stay armed. The heightmap comes with the first frames of a load
                if (m_Dirty)
                {
                    // A new trigger (re)starts the pass on the current edge list.
                    m_Dirty = false;
                    m_Passes++;
                    m_PassWhy = m_DirtyWhy;
                    m_Cursor = 0;
                    m_PassLanes = m_PassSamples = m_PassFrames = 0;
                    m_PassTicks = m_PassMaxTicks = 0;
                    m_LowNext.Clear();
                }
                long t0 = Stopwatch.GetTimestamp();
                int budget = m_PassSamples + SliceSamples;
                while (m_Cursor < m_Edges.Count && m_PassSamples < budget)
                    MeasureEdge(m_Edges[m_Cursor++], ref hd);
                long dt = Stopwatch.GetTimestamp() - t0;
                m_PassTicks += dt;
                m_PassMaxTicks = math.max(m_PassMaxTicks, dt);
                m_PassFrames++;
                if (m_Cursor >= m_Edges.Count)
                {
                    m_Cursor = -1;
                    m_LastPassFrame = m_Frame;
                    Finish();
                }
            }
            catch (Exception e)
            {
                m_Disabled = true;
                Mod.Log.Error($"!!! Towers [clearance]: {e.GetType().Name}: {e.Message} — disabled for this load (the hub keeps its last " +
                              $"low-span rows)\n{e}");
            }
        }

        private bool LiftEdgeUpdated()
        {
            IReadOnlyDictionary<Entity, LiftTypeDef> lines = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>().LiftLineTypes;
            NativeArray<Entity> upd = m_UpdatedEdges.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < upd.Length; i++)
                    if (lines.ContainsKey(EntityManager.GetComponentData<PrefabRef>(upd[i]).m_Prefab))
                        return true;
                return false;
            }
            finally
            {
                upd.Dispose();
            }
        }

        private LiftTowerPlanConstants ConstantsOf(byte type)
        {
            if (!m_Constants.TryGetValue(type, out LiftTowerPlanConstants c))
            {
                c = LiftTowerPlanConstants.For(SkiCatalog.LiftByIndex(type));
                m_Constants[type] = c;
            }
            return c;
        }

        /// <summary>One edge into m_LowNext when its lowest carrier passes more than TolM under the floor.</summary>
        private void MeasureEdge(LiftCensus.Edge ce, ref TerrainHeightData hd)
        {
            Entity edge = LiftCensusSystem.Ent(ce.Id);
            if (ce.Type == LiftCensus.NoType || !EntityManager.Exists(edge) || EntityManager.HasComponent<Deleted>(edge) ||
                EntityManager.HasComponent<Temp>(edge) || !EntityManager.TryGetBuffer(edge, true, out DynamicBuffer<SubLane> subLanes))
                return;                               // a newer census build carries the replacement
            LiftTowerPlanConstants c = ConstantsOf(ce.Type);
            if (c == null)
                return;
            bool stationStart = m_Census.IsStationNode(LiftCensusSystem.Ent(ce.Start));
            bool stationEnd = m_Census.IsStationNode(LiftCensusSystem.Ent(ce.End));
            bool low = false;
            Low worst = new Low { ClearM = float.MaxValue };
            for (int l = 0; l < subLanes.Length; l++)
            {
                Entity lane = subLanes[l].m_SubLane;
                if (EntityManager.HasComponent<Deleted>(lane) || !EntityManager.TryGetComponent(lane, out Curve curve) ||
                    !EntityManager.TryGetComponent(lane, out PrefabRef lanePrefab) ||
                    !EntityManager.TryGetComponent(lanePrefab.m_Prefab, out UtilityLaneData laneData) ||
                    (laneData.m_UtilityTypes & UtilityTypes.HighVoltageLine) == 0)
                    continue;
                m_PassLanes++;
                int n = math.clamp((int)math.ceil(curve.m_Length / c.StepM), 4, MaxSamples);
                for (int q = 0; q <= n; q++)
                {
                    float3 rope = MathUtils.Position(curve.m_Bezier, q / (float)n);
                    if ((stationStart && math.distance(rope.xz, ce.StartPos.xz) < c.StationClearM) ||
                        (stationEnd && math.distance(rope.xz, ce.EndPos.xz) < c.StationClearM))
                        continue;
                    m_PassSamples++;
                    float3 carrier = new float3(rope.x, rope.y - c.CarrierDropM, rope.z);
                    float clear = carrier.y - TerrainUtils.SampleHeight(ref hd, carrier);
                    if (clear < worst.ClearM)
                        worst = new Low { ClearM = clear, At = carrier };
                    if (clear < c.FloorM - TolM)
                        low = true;
                }
            }
            if (low)
                m_LowNext[edge] = worst;
        }

        private void Finish()
        {
            // The low spans: when the set or a span's figure changed, log it and have the census re-measure. So the hub's LiftLowSpan row
            // follows the line within one coalesce window (the census reads m_Low at its harvest).
            if (!SameLow(m_Low, m_LowNext))
            {
                (m_Low, m_LowNext) = (m_LowNext, m_Low);
                double ms = m_PassTicks * 1000.0 / Stopwatch.Frequency, maxMs = m_PassMaxTicks * 1000.0 / Stopwatch.Frequency;
                Low w = new Low { ClearM = float.MaxValue };
                Entity we = Entity.Null;
                foreach (KeyValuePair<Entity, Low> kv in m_Low)
                    if (kv.Value.ClearM < w.ClearM)
                    {
                        w = kv.Value;
                        we = kv.Key;
                    }
                Mod.Log.Info($"Towers [clearance]: pass {m_Passes} ({m_PassWhy}) {m_Low.Count} span(s) whose carriers pass under the floor " +
                             $"(3 m, tram 5 m, less {TolM:F1})" +
                             (m_Low.Count > 0 ? $", worst {w.ClearM:F2} m above the ground on edge {we.Index}:{we.Version} at " +
                                                $"({w.At.x:F0}, {w.At.z:F0})" : string.Empty) +
                             $"; {m_PassLanes} rope lane(s), {m_PassSamples} samples, {ms:F2} ms over {m_PassFrames} frame(s), at most {maxMs:F2} " +
                             "ms in one (hub: LiftLowSpan)");
                m_Census?.RequestBuild("low spans");
            }
        }

        private static bool SameLow(Dictionary<Entity, Low> a, Dictionary<Entity, Low> b)
        {
            if (a.Count != b.Count)
                return false;
            foreach (KeyValuePair<Entity, Low> kv in a)
                if (!b.TryGetValue(kv.Key, out Low v) || math.abs(v.ClearM - kv.Value.ClearM) >= 0.05f)
                    return false;
            return true;
        }
    }
}
