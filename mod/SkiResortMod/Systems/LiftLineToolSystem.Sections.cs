// The lift-line tool's section delete (DECISIONS 591, F1). In the first pick step (nothing drawn yet) the tool aims at the rope lanes, the
// curves the player sees. It removes the section under the cursor through the vanilla delete definition (the one
// BulldozeToolSystem.AddEntity writes for an edge, BulldozeToolSystem.cs:731-780). So validation, refund and apply are the game's.
// The pure ray-vs-rope pick is LiftRopePick (LiftBatch RP-1..RP-9). This file holds:
//   * the rope curves, cached and refreshed per census build.
//   * the cursor ray.
//   * a vanilla Net raycast as the fallback when no rope is near.
//   * the game's own delete preview (Temp) standing on the hovered section.
//   * the apply on a click.
// Section-by-section editing stays the workflow: one edge goes, its neighbors stay, and the tool redraws the gap from the two loose ends.
// Engine notes: EngineNotes/LiftLineToolSystem.md, "Section delete by the rope".

using System.Collections.Generic;
using Colossal.Entities;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Rendering;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class LiftLineToolSystem
    {
        private const int RopeRefreshFrames = 120;     // re-read the rope curves at least this often while picking (the fit moves towers)
        private const int DeleteWaitFrames = 30;       // a click before its preview's Temps exist waits this long, then stops
        private const int SectionLogLines = 40;        // hover lines per session in full, then every 25th
        private static readonly UnityEngine.Color SectionColor = new UnityEngine.Color(1f, 0.3f, 0.25f, 0.95f);

        private struct RopeSection
        {
            public Entity Edge, Start, End;
            public LiftTypeDef Type;
            public float LengthM;
        }

        private CameraUpdateSystem m_CameraUpdate;
        private RaycastSystem m_RaycastSystem;
        private readonly object m_FallbackContext = new object();   // our own RaycastSystem input (RaycastSystem.cs:770-803)
        private bool m_FallbackQueued;
        private readonly List<float3> m_RopePoints = new List<float3>();
        private readonly List<LiftRopePick.Rope> m_Ropes = new List<LiftRopePick.Rope>();
        private readonly List<RopeSection> m_RopeSections = new List<RopeSection>();
        private readonly List<bool> m_RopeSectionOn = new List<bool>();
        private readonly List<LiftRopePick.Hit> m_RopeScratch = new List<LiftRopePick.Hit>();
        private readonly List<Entity> m_RopeOwners = new List<Entity>();
        private int m_RopeCensusBuilds = -1, m_RopeFrame = int.MinValue / 2;
        private string m_RopeCacheLog;
        private int m_HoverSection = -1;           // index into m_RopeSections, -1 = none
        private LiftRopePick.Hit m_HoverRope;      // the rope hit when picked by a rope
        private bool m_HoverBySheet;               // picked by the vanilla raycast fallback (no rope near)
        private float3 m_SheetHit;
        private Entity m_DelEdge = Entity.Null;    // the section whose delete definition stands (the game's preview)
        private int m_DelFrame;
        private Entity m_DelPending = Entity.Null; // clicked before its preview's Temps existed
        private int m_DelPendingFrame;
        private Entity m_LoggedHover = Entity.Null;
        private int m_SectionLogs, m_SectionsDeleted;

        /// <summary>A built section is under the cursor (first pick step only).</summary>
        internal bool SectionHovered => m_HoverSection >= 0 && m_HoverSection < m_RopeSections.Count;
        internal string HoverSectionTypeId => SectionHovered && m_RopeSections[m_HoverSection].Type != null ? m_RopeSections[m_HoverSection].Type.Id : string.Empty;
        internal float HoverSectionLengthM => SectionHovered ? m_RopeSections[m_HoverSection].LengthM : 0f;

        private void CreateSections()
        {
            m_CameraUpdate = World.GetOrCreateSystemManaged<CameraUpdateSystem>();
            m_RaycastSystem = World.GetOrCreateSystemManaged<RaycastSystem>();
        }

        /// <summary>Stop or load (the definitions are gone with it): forget the hover, the preview, a pending click and, on a load, the
        /// rope cache. A Reset while the tool runs only drops the hover (ResetSectionHover). The next pick update clears a standing preview
        /// when its section is no longer hovered.</summary>
        private void ResetSections(bool cache)
        {
            ResetSectionHover();
            m_HoverBySheet = false;
            m_DelEdge = m_DelPending = m_LoggedHover = Entity.Null;
            m_FallbackQueued = false;
            if (!cache)
                return;
            m_RopePoints.Clear();
            m_Ropes.Clear();
            m_RopeSections.Clear();
            m_RopeCensusBuilds = -1;
            m_RopeFrame = int.MinValue / 2;
            m_RopeCacheLog = null;
            m_SectionLogs = m_SectionsDeleted = 0;
        }

        private void ResetSectionHover()
        {
            m_HoverSection = -1;
            m_HoverBySheet = false;
        }

        /// <summary>Every lift-line edge's rope lanes as polylines (LiftRopePick.AddRope), rebuilt when the census rebuilt (a line drawn, a
        /// section deleted) and at least every RopeRefreshFrames while picking. Never per frame. The edges come from the census
        /// (CopyLineOwners: the line edges and their end nodes, nodes skipped), otherwise from the tool's own edge query.</summary>
        private void RefreshRopes(bool force)
        {
            int builds = m_Census.Builds;
            if (!force && builds == m_RopeCensusBuilds && m_Frame - m_RopeFrame < RopeRefreshFrames)
                return;
            m_RopeCensusBuilds = builds;
            m_RopeFrame = m_Frame;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            m_RopePoints.Clear();
            m_Ropes.Clear();
            m_RopeSections.Clear();
            string source = "census";
            if (m_Census.CopyLineOwners(m_RopeOwners) < 0)
            {
                source = "edge query (census not built)";
                m_RopeOwners.Clear();
                using (var edges = m_LineEdgeQuery.ToEntityArray(Allocator.Temp))
                    for (int i = 0; i < edges.Length; i++)
                        if (m_Catalog.LiftLineTypes.ContainsKey(EntityManager.GetComponentData<PrefabRef>(edges[i]).m_Prefab))
                            m_RopeOwners.Add(edges[i]);
            }
            int lanes = 0;
            for (int o = 0; o < m_RopeOwners.Count; o++)
            {
                Entity edge = m_RopeOwners[o];
                if (!EntityManager.TryGetComponent<Game.Net.Edge>(edge, out var ed) || !EdgeAlive(edge) ||
                    !EntityManager.TryGetComponent<PrefabRef>(edge, out var pr) || !m_Catalog.LiftLineTypes.TryGetValue(pr.m_Prefab, out LiftTypeDef def) ||
                    !EntityManager.TryGetBuffer(edge, true, out DynamicBuffer<Game.Net.SubLane> subLanes))
                    continue;
                int section = m_RopeSections.Count, before = m_Ropes.Count;
                for (int k = 0; k < subLanes.Length; k++)
                {
                    if (!TryRopeCurve(subLanes[k].m_SubLane, out Bezier4x3 rope))
                        continue;
                    m_Ropes.Add(LiftRopePick.AddRope(m_RopePoints, section, rope.a, rope.b, rope.c, rope.d));
                    lanes++;
                }
                if (m_Ropes.Count == before)
                    continue;
                // Horizontal, as the plan line counts a span ("Towers: 0, length 471 m"); Curve.m_Length is the 3D length (499 m there).
                float length = EntityManager.TryGetComponent<Curve>(edge, out var curve) ? math.distance(curve.m_Bezier.a.xz, curve.m_Bezier.d.xz) : 0f;
                m_RopeSections.Add(new RopeSection { Edge = edge, Start = ed.m_Start, End = ed.m_End, Type = def, LengthM = length });
            }
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            string sig = $"{m_RopeSections.Count} section(s), {lanes} rope(s), {m_RopePoints.Count} point(s)";
            if (sig != m_RopeCacheLog)
            {
                m_RopeCacheLog = sig;
                Mod.Log.Info($"LiftLine [section] rope cache from the {source}: {sig}, {ms:F2} ms (census build {builds})");
            }
        }

        /// <summary>A rope: a live UtilityLane whose prefab carries HighVoltageLine (the haul / track rope) or LowVoltageLine (the tram's
        /// haul rope) UtilityLaneData. Its Curve is the hanging curve LaneSystem wrote (LaneSystem.cs:4128-4138).</summary>
        private bool TryRopeCurve(Entity lane, out Bezier4x3 bezier)
        {
            bezier = default;
            if (lane == Entity.Null || !EntityManager.HasComponent<Game.Net.UtilityLane>(lane) || EntityManager.HasComponent<Deleted>(lane) ||
                EntityManager.HasComponent<Temp>(lane) || !EntityManager.TryGetComponent<PrefabRef>(lane, out var pr) ||
                !EntityManager.TryGetComponent<UtilityLaneData>(pr.m_Prefab, out var data) ||
                (data.m_UtilityTypes & (UtilityTypes.HighVoltageLine | UtilityTypes.LowVoltageLine)) == 0 ||
                !EntityManager.TryGetComponent<Curve>(lane, out var curve))
                return false;
            bezier = curve.m_Bezier;
            return true;
        }

        private bool EdgeAlive(Entity edge) =>
            edge != Entity.Null && EntityManager.Exists(edge) && !EntityManager.HasComponent<Deleted>(edge) && !EntityManager.HasComponent<Temp>(edge);

        /// <summary>First pick step: the section under the cursor. A rope within LiftRopePick's tolerance wins. With none near, the vanilla
        /// Net raycast of the last frame (the sheet the bulldozer hits) is the fallback. A loose end or a station that can still take a
        /// line keeps its click (the redraw starts there). A station that already has its line yields to the rope.</summary>
        private void UpdateSection(LiftTypeDef want, bool live)
        {
            m_HoverSection = -1;
            m_HoverBySheet = false;
            Entity sheetEdge = ReadFallback(out m_SheetHit);
            if (!live || !m_CameraUpdate.TryGetViewer(out Viewer viewer) || viewer.camera == null)
            {
                m_FallbackQueued = false;
                m_LoggedHover = Entity.Null;
                return;
            }
            Line3.Segment line = ToolRaycastSystem.CalculateRaycastLine(viewer.camera);   // the tool raycast's own line (ToolRaycastSystem.cs:69-78)
            QueueFallback(line);
            if (m_HoverLoose >= 0 || (m_HoverStation >= 0 && !StationHasLine(m_HoverStation)))
            {
                m_LoggedHover = Entity.Null;
                return;
            }
            RefreshRopes(false);
            m_RopeSectionOn.Clear();
            for (int i = 0; i < m_RopeSections.Count; i++)
                m_RopeSectionOn.Add(want == null || m_RopeSections[i].Type == want);
            float fovTan = math.tan(math.radians(viewer.camera.fieldOfView) * 0.5f);   // as RaycastSystem.PerformRaycast (RaycastSystem.cs:857)
            float maxCam = m_HasHit ? math.distance(line.a, m_Hit) : math.distance(line.a, line.b);
            if (LiftRopePick.Pick(m_RopePoints, m_Ropes, m_RopeSectionOn, line.a, line.b, fovTan, maxCam, m_RopeScratch,
                    out LiftRopePick.Hit hit, out int tested))
            {
                if (EdgeAlive(m_RopeSections[hit.Section].Edge))
                {
                    m_HoverSection = hit.Section;
                    m_HoverRope = hit;
                }
                else
                {
                    m_RopeFrame = int.MinValue / 2;   // a deleted edge still cached: re-read next frame
                }
            }
            else if (sheetEdge != Entity.Null && EdgeAlive(sheetEdge))
            {
                for (int i = 0; i < m_RopeSections.Count; i++)
                {
                    if (m_RopeSections[i].Edge != sheetEdge || !m_RopeSectionOn[i])
                        continue;
                    m_HoverSection = i;
                    m_HoverBySheet = true;
                    break;
                }
            }
            if (m_HoverSection >= 0)
            {
                m_HoverStation = -1;   // a station with its line (or none) under the rope: the rope wins
                LogHover(tested);
            }
            else
            {
                m_LoggedHover = Entity.Null;
            }
        }

        /// <summary>The station already has a lift edge at its stub node (StationEnd would refuse it), read without a notice.</summary>
        private bool StationHasLine(int index)
        {
            Entity station = m_Census.StationEntityAt(index);
            return TryStubNode(station, m_Census.StationAt(index).StubTip, out Entity node, quiet: true) && LineEdgesAt(node) > 0;
        }

        /// <summary>Last frame's fallback result: the lift edge the vanilla Net raycast hit (its sheet: EdgeGeometry + m_SurfaceHeight.max,
        /// Game.Net/RaycastJobs.cs:437-460), or a node hit on an edge's end handed to that edge as the bulldozer does
        /// (BulldozeToolSystem.cs:1660-1663).</summary>
        private Entity ReadFallback(out float3 hitPos)
        {
            hitPos = default;
            if (!m_FallbackQueued)
                return Entity.Null;
            NativeArray<RaycastResult> results = m_RaycastSystem.GetResult(m_FallbackContext);
            if (!results.IsCreated || results.Length == 0 || results[0].m_Owner == Entity.Null)
                return Entity.Null;
            RaycastResult r = results[0];
            Entity e = r.m_Owner;
            if (!EntityManager.HasComponent<Game.Net.Edge>(e) && r.m_Hit.m_HitEntity != Entity.Null && EntityManager.HasComponent<Game.Net.Edge>(r.m_Hit.m_HitEntity))
                e = r.m_Hit.m_HitEntity;
            hitPos = r.m_Hit.m_HitPosition;
            return EntityManager.HasComponent<Game.Net.Edge>(e) ? e : Entity.Null;
        }

        /// <summary>One Net raycast on the cursor line for the next frame. It is the bulldozer's net test (BulldozeToolSystem.cs:1457-1470:
        /// TypeMask.Net, OnGround | Overground), limited to the HV layer the lift lines connect on (ValidateResult,
        /// Game.Net/RaycastJobs.cs:562-563). It uses its own RaycastSystem input (AddInput / GetResult by context,
        /// RaycastSystem.cs:770-803). So the tool's terrain raycast and every pick built on it stay as they are.</summary>
        private void QueueFallback(Line3.Segment line)
        {
            m_RaycastSystem.AddInput(m_FallbackContext, new RaycastInput
            {
                m_Line = line,
                m_TypeMask = TypeMask.Net,
                m_CollisionMask = CollisionMask.OnGround | CollisionMask.Overground,
                m_NetLayerMask = Layer.PowerlineHigh,
            });
            m_FallbackQueued = true;
        }

        /// <summary>The preview follows the hover: the hovered section's delete definition stands (its Temps are the game's own delete
        /// preview) and is replaced only when the hover changes (applyMode Clear + destroy + emit in one frame, the bulldozer's pattern,
        /// BulldozeToolSystem.cs:1686-1739). A pending click keeps its section's definition.</summary>
        private void SyncSectionPreview()
        {
            Entity target = m_DelPending != Entity.Null ? m_DelPending : SectionHovered ? m_RopeSections[m_HoverSection].Edge : Entity.Null;
            if (target == m_DelEdge)
                return;
            ClearSectionPreview(null);
            if (target == Entity.Null || !EdgeAlive(target))
                return;
            applyMode = ApplyMode.Clear;
            EmitDelete(target);
            m_DelEdge = target;
            m_DelFrame = m_Frame;
        }

        private void ClearSectionPreview(string why)
        {
            if (m_DelEdge == Entity.Null)
                return;
            applyMode = ApplyMode.Clear;   // ToolClearSystem deletes the preview's Temps and shows the original again (ToolClearSystem.cs:63-113)
            m_Deps = DestroyDefinitions(m_DefinitionQuery, m_Barrier, m_Deps);
            m_DelEdge = Entity.Null;
            if (why != null)
                Mod.Log.Info($"LiftLine [section] preview cleared ({why})");
        }

        /// <summary>The vanilla delete definition of one unowned edge, field by field as BulldozeToolSystem.AddEntity writes it
        /// (BulldozeToolSystem.cs:731-750 CreationDefinition m_Original + CreationFlags.Delete, Updated, with :756-779 the NetCourse from
        /// the edge's curve and its two nodes). No OwnerDefinition: a lift line has no owner (the census and the loose-end search take
        /// edges without Owner). No Fixed index: lift lines are power-line nets, never Fixed (AddFixedEdges, :690-729, finds nothing).
        /// GenerateEdgesSystem turns it into a Temp with TempFlags.Delete and m_Original (GenerateEdgesSystem.cs:1371-1376).</summary>
        private void EmitDelete(Entity edge)
        {
            Game.Net.Edge ed = EntityManager.GetComponentData<Game.Net.Edge>(edge);
            Bezier4x3 curve = EntityManager.GetComponentData<Curve>(edge).m_Bezier;
            var course = new NetCourse
            {
                m_Curve = curve,
                m_Length = MathUtils.Length(curve),
                m_FixedIndex = -1,
            };
            course.m_StartPosition.m_Entity = ed.m_Start;
            course.m_StartPosition.m_Position = curve.a;
            course.m_StartPosition.m_Rotation = NetUtils.GetNodeRotation(MathUtils.StartTangent(curve));
            course.m_StartPosition.m_CourseDelta = 0f;
            course.m_EndPosition.m_Entity = ed.m_End;
            course.m_EndPosition.m_Position = curve.d;
            course.m_EndPosition.m_Rotation = NetUtils.GetNodeRotation(MathUtils.EndTangent(curve));
            course.m_EndPosition.m_CourseDelta = 1f;
            EntityCommandBuffer ecb = m_Barrier.CreateCommandBuffer();
            Entity e = ecb.CreateEntity();
            ecb.AddComponent(e, new CreationDefinition { m_Original = edge, m_Flags = CreationFlags.Delete });
            ecb.AddComponent(e, default(Updated));
            ecb.AddComponent(e, course);
        }

        /// <summary>A click on a hovered section: delete it when its preview stands, else wait for the preview (DeleteWaitFrames).</summary>
        private void ClickSection()
        {
            if (!SectionHovered)
                return;
            m_DelPending = m_RopeSections[m_HoverSection].Edge;
            m_DelPendingFrame = m_Frame;
        }

        /// <summary>True on the frame it applies (no preview change may follow in that frame). Applies a pending delete once the game's
        /// preview of it exists: applyMode Apply for one frame and the definition destroyed, as BulldozeToolSystem.Apply
        /// (BulldozeToolSystem.cs:1618-1640) after its GetAllowApply (no Error entity, the original not deleted meanwhile,
        /// ToolBaseSystem.cs:533-540). ApplyNetSystem then deletes the edge, pays the refund (CostSystem.cs:189, ApplyNetSystem.cs:662) and
        /// leaves both end nodes to their other sections.</summary>
        private bool UpdatePendingDelete()
        {
            if (m_DelPending == Entity.Null)
                return false;
            Entity edge = m_DelPending;
            if (!EdgeAlive(edge))
            {
                Mod.Log.Info($"LiftLine [section] {Fmt(edge)} is gone before its delete applied — nothing to do");
                m_DelPending = Entity.Null;
                return false;
            }
            if (m_DelEdge != edge || !TempDeleteStands(edge))
            {
                if (m_Frame - m_DelPendingFrame > DeleteWaitFrames)
                {
                    Mod.Log.Warn($"LiftLine [section] no delete preview of {Fmt(edge)} {DeleteWaitFrames} frames after the click — not removed " +
                                 "(please send the log)");
                    m_Notice = T("SECTION_REFUSED");
                    m_NoticeTime = UnityEngine.Time.unscaledTime;
                    m_DelPending = Entity.Null;
                }
                return false;
            }
            int index = SectionIndex(edge);
            RopeSection s = index >= 0 ? m_RopeSections[index] : new RopeSection { Edge = edge };
            if (!base.GetAllowApply())
            {
                string kinds = DescribeErrors(out string summary, out _);
                Mod.Log.Warn($"LiftLine [section] the game refuses to remove {Fmt(edge)} ({summary}) — not removed{kinds}");
                m_Notice = T("SECTION_REFUSED");
                m_NoticeTime = UnityEngine.Time.unscaledTime;
                m_DelPending = Entity.Null;
                return false;
            }
            applyMode = ApplyMode.Apply;   // ToolOutputSystem.cs:22-29, then ApplyNetSystem
            m_Deps = DestroyDefinitions(m_DefinitionQuery, m_Barrier, m_Deps);
            m_DelEdge = m_DelPending = Entity.Null;
            m_SectionsDeleted++;
            m_RopeFrame = int.MinValue / 2;    // the rope cache and the loose ends change with it
            m_LooseFrame = int.MinValue / 2;
            Say($"LiftLine [section] DELETED {Fmt(edge)} ({(s.Type != null ? s.Type.Id : "?")}, {s.LengthM:F0} m, nodes {Fmt(s.Start)} / " +
                $"{Fmt(s.End)}) through the vanilla delete definition, {m_Frame - m_DelPendingFrame} frame(s) after the click; picked " +
                (m_HoverBySheet || index < 0 ? "by the vanilla sheet fallback" : $"by the rope, miss {m_HoverRope.MissM:F2} m of {m_HoverRope.TolM:F2} m") +
                $"; {m_SectionsDeleted} section(s) removed this session");
            return true;
        }

        private int SectionIndex(Entity edge)
        {
            for (int i = 0; i < m_RopeSections.Count; i++)
                if (m_RopeSections[i].Edge == edge)
                    return i;
            return -1;
        }

        /// <summary>The game made the Temp of this edge's delete definition (TempFlags.Delete, m_Original = the edge).</summary>
        private bool TempDeleteStands(Entity edge)
        {
            using (var temps = m_TempEdgeQuery.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < temps.Length; i++)
                {
                    Temp t = EntityManager.GetComponentData<Temp>(temps[i]);
                    if (t.m_Original == edge && (t.m_Flags & TempFlags.Delete) != 0)
                        return true;
                }
            }
            return false;
        }

        private void LogHover(int tested)
        {
            RopeSection s = m_RopeSections[m_HoverSection];
            if (s.Edge == m_LoggedHover)
                return;
            m_LoggedHover = s.Edge;
            m_SectionLogs++;
            if (m_SectionLogs > SectionLogLines && m_SectionLogs % 25 != 0)
                return;
            string how = m_HoverBySheet
                ? $"by the vanilla sheet fallback at ({m_SheetHit.x:F1}, {m_SheetHit.y:F1}, {m_SheetHit.z:F1}) (no rope within tolerance)"
                : $"by the rope: miss {m_HoverRope.MissM:F2} m of {m_HoverRope.TolM:F2} m at {m_HoverRope.CamM:F0} m from the camera, rope point " +
                  $"({m_HoverRope.RopePoint.x:F1}, {m_HoverRope.RopePoint.y:F2}, {m_HoverRope.RopePoint.z:F1}) at {m_HoverRope.RopeT:F2} of the rope; " +
                  $"{tested} segment(s) tested of {m_RopePoints.Count - m_Ropes.Count}";
            Mod.Log.Info($"LiftLine [section] hover {Fmt(s.Edge)} ({(s.Type != null ? s.Type.Id : "?")}, {s.LengthM:F0} m, nodes {Fmt(s.Start)} / " +
                         $"{Fmt(s.End)}) {how}{(m_SectionLogs == SectionLogLines ? " (further hover lines: every 25th)" : string.Empty)}");
        }

        /// <summary>The hovered section's ropes in the pick overlay, in the bulldozer's red, about 3 px wide at any distance.</summary>
        private void DrawSection(OverlayRenderSystem.Buffer buffer)
        {
            if (!SectionHovered)
                return;
            float width = m_HoverBySheet ? 1f : math.clamp(m_HoverRope.TolM * 0.25f, 0.4f, 8f);
            var round = new float2(1f, 1f);
            for (int r = 0; r < m_Ropes.Count; r++)
            {
                LiftRopePick.Rope rope = m_Ropes[r];
                if (rope.Section != m_HoverSection)
                    continue;
                for (int i = 0; i < rope.Count - 1; i++)
                    buffer.DrawLine(SectionColor, SectionColor, 0f, (OverlayRenderSystem.StyleFlags)0, new Line3.Segment(m_RopePoints[rope.First + i], m_RopePoints[rope.First + i + 1]),
                        width, round);
            }
        }
    }
}
