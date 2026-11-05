// The lift-line tool's tree corridor (DECISIONS 601 e, owner hand-test note 8). With every emission of the line's courses (the Edit
// ghost and the Build) the tool also emits the vanilla bulldoze definition for each tree in the corridor (LiftTreeCorridor, the pure
// rule): SkiGladeToolSystem.EmitThinDefinition, the glade tool's fell (BulldozeToolSystem.AddEntity's shape for an unowned static
// object). The ghost shows the cut (GenerateObjectsSystem hides the original under its Delete Temp); Build applies it together with the
// line (ApplyObjectsSystem adds Deleted to the original), so the trees save, refund and validate like a vanilla bulldoze, and a refused
// or dropped line keeps every tree (ToolClearSystem shows the originals again). The frame after the apply reports what fell:
// "LiftLine [trees]: felled N tree(s) in a W m corridor".
// Engine notes: EngineNotes/LiftTreeCorridor.md

using System.Collections.Generic;
using Colossal.Collections;
using Colossal.Entities;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class LiftLineToolSystem
    {
        private Game.Objects.SearchSystem m_TreeSearch;
        private readonly List<LiftTreeCorridor.Segment> m_FellLine = new List<LiftTreeCorridor.Segment>();
        private readonly HashSet<Entity> m_FellSeen = new HashSet<Entity>();
        private readonly List<Entity> m_Felled = new List<Entity>();   // the originals of the last emission's tree Delete definitions
        private int m_FellCapped, m_FellStubEdges;

        /// <summary>The corridor width of the current type (0 with no type: the logs never throw).</summary>
        private float CorridorWidthM => m_Type != null ? LiftTreeCorridor.WidthM(m_Type) : 0f;

        /// <summary>The corridor of the emitted nodes (plus the station ends' own stub edges): one Delete definition per tree that
        /// LiftTreeCorridor fells, at most MaxFellPerBuild. Fills m_Felled. Returns the count emitted.</summary>
        private int EmitCorridorFells(EntityCommandBuffer ecb, List<LiftLineNode> nodes)
        {
            m_Felled.Clear();
            m_FellCapped = 0;
            m_FellStubEdges = 0;
            if (m_Type == null || nodes.Count < 2)
                return 0;
            m_FellLine.Clear();
            for (int i = 0; i < nodes.Count - 1; i++)
                m_FellLine.Add(new LiftTreeCorridor.Segment(nodes[i].Position.xz, nodes[i + 1].Position.xz));
            m_FellStubEdges = AddStubEdges(m_Base) + AddStubEdges(m_Top);

            if (m_TreeSearch == null)
                m_TreeSearch = World.GetOrCreateSystemManaged<Game.Objects.SearchSystem>();
            // Synchronous read: complete the writer deps (the glade tool's CensusTrees idiom, Game.Objects/SearchSystem.cs:421-425)
            NativeQuadTree<Entity, QuadTreeBoundsXZ> tree = m_TreeSearch.GetStaticSearchTree(readOnly: true, out JobHandle treeDeps);
            treeDeps.Complete();

            float reach = LiftTreeCorridor.QueryReachM(m_Type);
            float envelope = LiftTreeCorridor.EnvelopeHalfM(m_Type);
            m_FellSeen.Clear();
            var results = new NativeList<Entity>(64, Allocator.Temp);
            try
            {
                for (int s = 0; s < m_FellLine.Count; s++)
                {
                    float2 a = m_FellLine[s].A, b = m_FellLine[s].B;
                    results.Clear();
                    var it = new SkiGladeToolSystem.TreeBoundsIterator
                    {
                        m_Bounds = new Bounds2(math.min(a, b) - reach, math.max(a, b) + reach),
                        m_UseTriangle = false,
                        m_Results = results,
                    };
                    tree.Iterate(ref it);
                    for (int r = 0; r < results.Length; r++)
                    {
                        Entity e = results[r];
                        if (!m_FellSeen.Add(e) || !Fellable(e, out float2 trunk, out float crown))
                            continue;
                        if (!LiftTreeCorridor.Fells(LiftTreeCorridor.DistanceM(trunk, m_FellLine), crown, envelope))
                            continue;
                        if (m_Felled.Count >= LiftTreeCorridor.MaxFellPerBuild)
                        {
                            m_FellCapped++;
                            continue;
                        }
                        if (SkiGladeToolSystem.EmitThinDefinition(EntityManager, ecb, e, out _))
                            m_Felled.Add(e);
                    }
                }
            }
            finally
            {
                results.Dispose();
            }
            return m_Felled.Count;
        }

        /// <summary>A tree the corridor may fell: the glade tool's IsThinnable minus its run-member rule (a lift corridor fells planted
        /// glade trees too). Not Deleted (it lingers in the static tree until Modification5, SearchSystem.cs:91-99), no Owner (a
        /// building's or net's own tree; its delete could block the whole apply, ValidationSystem.cs:920-929), not burning, a TreeData
        /// prefab. Crown = the prefab bounds' widest xz reach from the pivot.</summary>
        private bool Fellable(Entity e, out float2 trunk, out float crown)
        {
            trunk = default;
            crown = 0f;
            if (EntityManager.HasComponent<Deleted>(e) || EntityManager.HasComponent<Owner>(e) ||
                EntityManager.HasComponent<Game.Events.OnFire>(e))
                return false;
            if (!EntityManager.TryGetComponent(e, out PrefabRef pr) || !EntityManager.HasComponent<TreeData>(pr.m_Prefab))
                return false;
            if (!EntityManager.TryGetComponent(e, out Game.Objects.Transform tr))
                return false;
            trunk = tr.m_Position.xz;
            if (EntityManager.TryGetComponent(pr.m_Prefab, out ObjectGeometryData geo))
                crown = math.cmax(math.max(math.abs(geo.m_Bounds.min.xz), math.abs(geo.m_Bounds.max.xz)));
            return true;
        }

        /// <summary>A station end: the station's own edges at its stub node (the carriers ride them into the station), as corridor
        /// segments (each curve in 4 pieces). Ground and loose ends add nothing: the round end of the last segment covers a ground end,
        /// and a loose end's edge belongs to the neighboring section.</summary>
        private int AddStubEdges(LineEnd end)
        {
            if (end.Kind != LiftLineEndKind.Station || end.Station == Entity.Null || !EntityManager.Exists(end.Node) ||
                !EntityManager.TryGetBuffer(end.Node, true, out DynamicBuffer<ConnectedEdge> edges))
                return 0;
            int added = 0;
            for (int i = 0; i < edges.Length; i++)
            {
                Entity edge = edges[i].m_Edge;
                if (!EntityManager.TryGetComponent(edge, out Owner owner) || owner.m_Owner != end.Station ||
                    !EntityManager.TryGetComponent(edge, out Curve curve))
                    continue;
                float2 prev = curve.m_Bezier.a.xz;
                for (int k = 1; k <= 4; k++)
                {
                    float2 next = MathUtils.Position(curve.m_Bezier, k * 0.25f).xz;
                    m_FellLine.Add(new LiftTreeCorridor.Segment(prev, next));
                    prev = next;
                }
                added++;
            }
            return added;
        }

        /// <summary>The frame after the apply: one line with what fell (an original that is gone or Deleted), WARN for any that stayed
        /// (a canceled Delete Temp) or the cap.</summary>
        private void ReportFelled()
        {
            if (m_Type == null)
                return;
            int felled = 0, standing = 0;
            for (int i = 0; i < m_Felled.Count; i++)
            {
                Entity e = m_Felled[i];
                if (!EntityManager.Exists(e) || EntityManager.HasComponent<Deleted>(e))
                    felled++;
                else
                    standing++;
            }
            string line = $"LiftLine [trees]: felled {felled} tree(s) in a {LiftTreeCorridor.WidthM(m_Type):F1} m corridor ({m_Type.Id}: rope " +
                          $"gauge +-{m_Type.GaugeHalfM:F1} m, carrier half width {m_Type.CarrierHalfWidthM:F1} m, margin {LiftTreeCorridor.MarginM:F1} m; " +
                          $"{m_FellLine.Count} segment(s), {m_FellStubEdges} station stub edge(s))";
            if (standing > 0 || m_FellCapped > 0)
                Mod.Log.Warn(line + $" — {standing} marked tree(s) still standing after the apply (a canceled delete), {m_FellCapped} left " +
                             $"over the cap of {LiftTreeCorridor.MaxFellPerBuild}");
            else
                Mod.Log.Info(line);
            m_Felled.Clear();
        }
    }
}
