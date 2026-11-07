// The thick outline and corner dots of a ski run while it is drawn, edited or bulldozed. We draw them as an overlay (the game's own
// AreaBorderRenderSystem pass, wider, for our four run prefabs only). The "Run outline size" setting means only "how thick".
// This system NEVER writes AreaGeometryData / m_SnapDistance: that value also drives snapping and the invalid-shape clearance.
// Phase Rendering, UpdateBefore<SkiRunOutlineSystem, OverlayRenderSystem> (Mod.cs). Load-bearing: the overlay system completes
// every registered writer, so our job must be registered before it in the same frame. The job joins base.Dependency.
// The per-frame guard is necessary, not sufficient: any Temp or flagged Surface passes it (the job returns early if none is ours).
// Logs: ARMED once per load, a cost line, a slow-draw WARN, a one-shot "!!!" that switches the overlay off. Nothing per frame.
// Engine notes: EngineNotes/SkiRunOutlineSystem.md

using System;
using System.Diagnostics;
using System.Globalization;
using Colossal.Entities;                 // EntityManager.TryGetComponent
using Colossal.Mathematics;              // Line3
using Colossal.Serialization.Entities;   // Purpose
using Game;
using Game.Areas;
using Game.Common;
using Game.Prefabs;
using Game.Rendering;
using Game.Tools;
using SkiResortMod.Settings;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiRunOutlineSystem : GameSystemBase
    {
        /// <summary>Our four run SurfacePrefabs (house practice: the table is duplicated per system. It uses the
        /// same names as SkiRunOwnershipSystem's *SurfaceNames).</summary>
        private static readonly string[] RunSurfaceNames =
            { "SkiRunGroomedA", "SkiRunSnowmakingA", "SkiRunNightA", "SkiRunNaturalA" };

        private static class Tuning
        {
            public const int CostLogAfterDraws = 600;
            public const float SlowDrawWarnMs = 0.5f;    // main-thread part of one draw
        }

        /// <summary>The game's borderMap key (AreaBorderRenderSystem.cs:23-38, copied).</summary>
        private struct Border : IEquatable<Border>
        {
            public float3 m_StartPos;
            public float3 m_EndPos;

            public bool Equals(Border other)
            {
                return m_StartPos.Equals(other.m_StartPos) & m_EndPos.Equals(other.m_EndPos);
            }

            public override int GetHashCode()
            {
                return m_StartPos.GetHashCode();
            }
        }

        /// <summary>One area's Node buffer as RunOutlineRules reads it.</summary>
        private struct NodeSource : IOutlineNodes
        {
            public DynamicBuffer<Game.Areas.Node> Buffer;
            public int Count => Buffer.Length;
            public float3 Get(int index) => Buffer[index].m_Position;
        }

        /// <summary>The job's borderMap, nodeMap and overlay writes (engine notes WHAT IT DRAWS).</summary>
        private struct OverlaySink : IOutlineSink
        {
            public NativeParallelHashSet<Border> Borders;
            public NativeParallelHashSet<float3> Dots;
            public OverlayRenderSystem.Buffer Overlay;
            public UnityEngine.Color LineColour;
            public UnityEngine.Color DotFill, DotRim;
            public float RimWidth;
            public float Width;
            public float Diameter;

            public void AddBorder(float3 start, float3 end)
            {
                Borders.Add(new Border { m_StartPos = start, m_EndPos = end });
            }

            public bool HasBorder(float3 start, float3 end)
            {
                return Borders.Contains(new Border { m_StartPos = start, m_EndPos = end });
            }

            public bool AddDot(float3 position)
            {
                return Dots.Add(position);
            }

            public void Line(float3 a, float3 b)
            {
                // OverlayRenderSystem.cs:207-211. The game passes roundness 1 (AreaBorderRenderSystem.cs:531).
                Overlay.DrawLine(LineColour, LineColour, 0f, OverlayRenderSystem.StyleFlags.Projected,
                    new Line3.Segment(a, b), Width, new float2(1f, 1f));
            }

            public void Dot(float3 position)
            {
                // OverlayRenderSystem.cs:138 (the last argument is a diameter). Direction (0, 1) as :517.
                Overlay.DrawCircle(DotRim, DotFill, RimWidth, OverlayRenderSystem.StyleFlags.Projected,
                    new float2(0f, 1f), position, Diameter);
            }
        }

        /// <summary>The outline of our runs, one frame. Field layout follows AreaBorderRenderSystem.cs:40-97 (the
        /// Buffer field carries no attribute).</summary>
        [BurstCompile]
        private struct RunOutlineJob : IJob
        {
            [ReadOnly] public ComponentTypeHandle<Area> AreaType;
            [ReadOnly] public ComponentTypeHandle<Temp> TempType;
            [ReadOnly] public ComponentTypeHandle<Game.Tools.Error> ErrorType;
            [ReadOnly] public ComponentTypeHandle<Game.Tools.Warning> WarningType;
            [ReadOnly] public ComponentTypeHandle<PrefabRef> PrefabRefType;
            [ReadOnly] public BufferTypeHandle<Game.Areas.Node> NodeType;
            [ReadOnly] public ComponentLookup<AreaGeometryData> GeometryData;
            [ReadOnly] public NativeList<ArchetypeChunk> Chunks;
            [ReadOnly] public NativeList<ArchetypeChunk> RunChunks;  // our existing (non-Temp) run areas
            public Entity Run0, Run1, Run2, Run3;
            public float Width, Diameter;
            public UnityEngine.Color HoveredColour, OwnerColour, WarningColour, ErrorColour, DotFill, DotRim;
            public float RimWidth;
            public OverlayRenderSystem.Buffer Overlay;

            public void Execute()
            {
                // :101-116 — the set capacities, and nothing at all when no area of ours is in the frame.
                int nodes = 0;
                bool ours = false;
                for (int c = 0; c < Chunks.Length; c++)
                {
                    ArchetypeChunk chunk = Chunks[c];
                    NativeArray<Area> areas = chunk.GetNativeArray(ref AreaType);
                    NativeArray<PrefabRef> prefabs = chunk.GetNativeArray(ref PrefabRefType);
                    BufferAccessor<Game.Areas.Node> buffers = chunk.GetBufferAccessor(ref NodeType);
                    for (int i = 0; i < areas.Length; i++)
                    {
                        if (((byte)areas[i].m_Flags & RunOutlineRules.AreaSlave) != 0)
                            continue;
                        nodes += buffers[i].Length;
                        if (IsOurs(prefabs[i].m_Prefab))
                            ours = true;
                    }
                }
                if (!ours)
                    return;
                // The drawn areas' bounds (Temp Create / Modify of ours), and the room for the neighbor dots.
                float4 bounds = DrawnBounds();
                bool neighbours = RunOutlineRules.HasBounds(bounds);
                if (neighbours)
                    nodes += CountRunNodes();

                var sink = new OverlaySink
                {
                    Borders = new NativeParallelHashSet<Border>(math.max(nodes, 1), Allocator.Temp),
                    Dots = new NativeParallelHashSet<float3>(math.max(nodes, 1), Allocator.Temp),
                    Overlay = Overlay,
                    DotFill = DotFill,
                    DotRim = DotRim,
                    RimWidth = RimWidth,
                    Width = Width,
                    Diameter = Diameter,
                };
                for (int c = 0; c < Chunks.Length; c++)
                    FileBorders(Chunks[c], ref sink);   // :129-132
                for (int c = 0; c < Chunks.Length; c++)
                    DrawOurs(Chunks[c], ref sink);      // :133-136
                if (neighbours)
                    DrawNeighbourDots(bounds, ref sink);
                sink.Borders.Dispose();
                sink.Dots.Dispose();
            }

            /// <summary>xz bounds of every node of our areas that get dots this frame (Temp Create / Modify), empty when none.</summary>
            private float4 DrawnBounds()
            {
                float4 b = RunOutlineRules.EmptyBounds();
                for (int c = 0; c < Chunks.Length; c++)
                {
                    ArchetypeChunk chunk = Chunks[c];
                    NativeArray<Temp> temps = chunk.GetNativeArray(ref TempType);
                    if (temps.Length == 0)
                        continue;
                    bool error = chunk.Has(ref ErrorType);
                    bool warning = chunk.Has(ref WarningType);
                    NativeArray<Area> areas = chunk.GetNativeArray(ref AreaType);
                    NativeArray<PrefabRef> prefabs = chunk.GetNativeArray(ref PrefabRefType);
                    BufferAccessor<Game.Areas.Node> buffers = chunk.GetBufferAccessor(ref NodeType);
                    for (int i = 0; i < areas.Length; i++)
                    {
                        Entity prefab = prefabs[i].m_Prefab;
                        uint tempFlags = (uint)temps[i].m_Flags;
                        if (!IsOurs(prefab) || !RunOutlineRules.Dots(true, tempFlags) ||
                            !RunOutlineRules.Outlined(error, warning, true, tempFlags, (byte)areas[i].m_Flags) || HiddenIngame(prefab))
                            continue;
                        DynamicBuffer<Game.Areas.Node> nodes = buffers[i];
                        for (int k = 0; k < nodes.Length; k++)
                            b = RunOutlineRules.Extend(b, nodes[k].m_Position);
                    }
                }
                return b;
            }

            private int CountRunNodes()
            {
                int n = 0;
                for (int c = 0; c < RunChunks.Length; c++)
                {
                    BufferAccessor<Game.Areas.Node> buffers = RunChunks[c].GetBufferAccessor(ref NodeType);
                    for (int i = 0; i < buffers.Length; i++)
                        n += buffers[i].Length;
                }
                return n;
            }

            /// <summary>The same white dots at the nodes of our existing run areas within
            /// NeighbourMarginM of the drawn bounds. One dot per position per frame (the shared dot set).</summary>
            private void DrawNeighbourDots(float4 bounds, ref OverlaySink sink)
            {
                for (int c = 0; c < RunChunks.Length; c++)
                {
                    ArchetypeChunk chunk = RunChunks[c];
                    NativeArray<Area> areas = chunk.GetNativeArray(ref AreaType);
                    NativeArray<PrefabRef> prefabs = chunk.GetNativeArray(ref PrefabRefType);
                    BufferAccessor<Game.Areas.Node> buffers = chunk.GetBufferAccessor(ref NodeType);
                    for (int i = 0; i < areas.Length; i++)
                    {
                        Entity prefab = prefabs[i].m_Prefab;
                        if (((byte)areas[i].m_Flags & RunOutlineRules.AreaSlave) != 0 || !IsOurs(prefab) || HiddenIngame(prefab))
                            continue;
                        DynamicBuffer<Game.Areas.Node> nodes = buffers[i];
                        for (int k = 0; k < nodes.Length; k++)
                        {
                            float3 p = nodes[k].m_Position;
                            if (RunOutlineRules.InWindow(bounds, p, RunOutlineRules.NeighbourMarginM) && sink.AddDot(p))
                                sink.Dot(p);
                        }
                    }
                }
            }

            private bool IsOurs(Entity prefab)
            {
                return prefab != Entity.Null && (prefab == Run0 || prefab == Run1 || prefab == Run2 || prefab == Run3);
            }

            private bool HiddenIngame(Entity prefab)
            {
                // :190-194 / :440-444 (game mode: m_EditorMode false). A prefab without AreaGeometryData is not hidden.
                return GeometryData.TryGetComponent(prefab, out AreaGeometryData geometry) &&
                       (geometry.m_Flags & GeometryFlags.HiddenIngame) != 0;
            }

            /// <summary>AddBorders (:141-264), infoview false: every outlined area, any prefab.</summary>
            private void FileBorders(ArchetypeChunk chunk, ref OverlaySink sink)
            {
                bool error = chunk.Has(ref ErrorType);
                bool warning = chunk.Has(ref WarningType);
                NativeArray<Temp> temps = chunk.GetNativeArray(ref TempType);
                bool hasTemp = temps.Length != 0;
                if (!error && !warning && !hasTemp)
                    return;
                NativeArray<Area> areas = chunk.GetNativeArray(ref AreaType);
                NativeArray<PrefabRef> prefabs = chunk.GetNativeArray(ref PrefabRefType);
                BufferAccessor<Game.Areas.Node> buffers = chunk.GetBufferAccessor(ref NodeType);
                for (int i = 0; i < areas.Length; i++)
                {
                    uint tempFlags = hasTemp ? (uint)temps[i].m_Flags : 0u;
                    byte areaFlags = (byte)areas[i].m_Flags;
                    if (!RunOutlineRules.Outlined(error, warning, hasTemp, tempFlags, areaFlags) ||
                        HiddenIngame(prefabs[i].m_Prefab))
                        continue;
                    var source = new NodeSource { Buffer = buffers[i] };
                    RunOutlineRules.AddEdges(ref source, ref sink, areaFlags);
                }
            }

            /// <summary>DrawBorders (:357-513) for our run prefabs only.</summary>
            private void DrawOurs(ArchetypeChunk chunk, ref OverlaySink sink)
            {
                bool error = chunk.Has(ref ErrorType);
                bool warning = chunk.Has(ref WarningType);
                NativeArray<Temp> temps = chunk.GetNativeArray(ref TempType);
                bool hasTemp = temps.Length != 0;
                if (!error && !warning && !hasTemp)
                    return;
                NativeArray<Area> areas = chunk.GetNativeArray(ref AreaType);
                NativeArray<PrefabRef> prefabs = chunk.GetNativeArray(ref PrefabRefType);
                BufferAccessor<Game.Areas.Node> buffers = chunk.GetBufferAccessor(ref NodeType);
                for (int i = 0; i < areas.Length; i++)
                {
                    Entity prefab = prefabs[i].m_Prefab;
                    if (!IsOurs(prefab))
                        continue;
                    uint tempFlags = hasTemp ? (uint)temps[i].m_Flags : 0u;
                    byte areaFlags = (byte)areas[i].m_Flags;
                    if (!RunOutlineRules.Outlined(error, warning, hasTemp, tempFlags, areaFlags) || HiddenIngame(prefab))
                        continue;
                    switch (RunOutlineRules.ColourOf(error, warning, tempFlags))
                    {
                        case OutlineColour.Error: sink.LineColour = ErrorColour; break;
                        case OutlineColour.Warning: sink.LineColour = WarningColour; break;
                        case OutlineColour.Owner: sink.LineColour = OwnerColour; break;
                        default: sink.LineColour = HoveredColour; break;
                    }
                    var source = new NodeSource { Buffer = buffers[i] };
                    RunOutlineRules.DrawArea(ref source, ref sink, areaFlags, RunOutlineRules.Dots(hasTemp, tempFlags));
                }
            }
        }

        // Systems, queries, handles.
        private OverlayRenderSystem m_Overlay;
        private PrefabSystem m_PrefabSystem;
        private EntityQuery m_PrefabScanQuery;
        private EntityQuery m_RunAreaQuery;  // our existing run areas (neighbor dots)
        private EntityQuery m_BorderQuery;            // the game's (engine notes WHAT IT DRAWS)
        private EntityQuery m_SurfaceQuery;           // THE GUARD
        private EntityQuery m_RenderingSettingsQuery;
        private ComponentTypeHandle<Area> m_AreaType;
        private ComponentTypeHandle<Temp> m_TempType;
        private ComponentTypeHandle<Game.Tools.Error> m_ErrorType;
        private ComponentTypeHandle<Game.Tools.Warning> m_WarningType;
        private ComponentTypeHandle<PrefabRef> m_PrefabRefType;
        private BufferTypeHandle<Game.Areas.Node> m_NodeType;
        private ComponentLookup<AreaGeometryData> m_GeometryData;
        private JobHandle m_DrawHandle;

        // Per load.
        private readonly Entity[] m_Runs = new Entity[4];
        private int m_RunCount;
        private bool m_FlagsMatch;                    // RunOutlineRules' mirrors equal the game's enums (OnCreate)
        private bool m_Armed, m_Disabled, m_ErrorLogged, m_CostLogged, m_SlowDrawLogged;
        private int m_DrawnSize = -1;                 // the size of the last draw (a change logs at the next draw)

        // Per-load counters and timings (cost line).
        public int Draws;
        public float LastDrawMs, MaxDrawMs;
        private double m_DrawMsSum;
        private int m_CostDraws;
        private static bool s_FirstDrawDone;          // per PROCESS (the JIT of Schedule<RunOutlineJob> is paid once)

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Overlay = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_PrefabScanQuery = GetEntityQuery(ComponentType.ReadOnly<PrefabData>());
            // AreaBorderRenderSystem.cs:614-628, plus Node and PrefabRef (engine notes).
            m_BorderQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Area>(),
                    ComponentType.ReadOnly<Game.Areas.Node>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                Any = new[]
                {
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<Game.Tools.Error>(),
                    ComponentType.ReadOnly<Game.Tools.Warning>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Hidden>(),
                    ComponentType.ReadOnly<Deleted>(),
                },
            });
            m_RunAreaQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Area>(),
                    ComponentType.ReadOnly<Game.Areas.Surface>(),
                    ComponentType.ReadOnly<Game.Areas.Node>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<Hidden>(),
                    ComponentType.ReadOnly<Deleted>(),
                },
            });
            // Every SurfacePrefab archetype carries Game.Areas.Surface (Game.Prefabs/SurfacePrefab.cs:21-23).
            m_SurfaceQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Area>(),
                    ComponentType.ReadOnly<Game.Areas.Surface>(),
                    ComponentType.ReadOnly<Game.Areas.Node>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                Any = new[]
                {
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<Game.Tools.Error>(),
                    ComponentType.ReadOnly<Game.Tools.Warning>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Hidden>(),
                    ComponentType.ReadOnly<Deleted>(),
                },
            });
            m_RenderingSettingsQuery = GetEntityQuery(ComponentType.ReadOnly<RenderingSettingsData>());
            // Handles created once (they register their types as read here, so OnUpdate adds no sync point) and
            // refreshed every draw with Update(this).
            m_AreaType = GetComponentTypeHandle<Area>(isReadOnly: true);
            m_TempType = GetComponentTypeHandle<Temp>(isReadOnly: true);
            m_ErrorType = GetComponentTypeHandle<Game.Tools.Error>(isReadOnly: true);
            m_WarningType = GetComponentTypeHandle<Game.Tools.Warning>(isReadOnly: true);
            m_PrefabRefType = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
            m_NodeType = GetBufferTypeHandle<Game.Areas.Node>(isReadOnly: true);
            m_GeometryData = GetComponentLookup<AreaGeometryData>(isReadOnly: true);

            m_FlagsMatch = FlagMirrorsMatch(out string mismatch);
            if (!m_FlagsMatch)
                Mod.Log.Warn($"RunOutline: the game's {mismatch} no longer equals RunOutlineRules — our run outline stays " +
                             "off for this session (the game's thin outline remains); RunOutlineRules needs updating");
            try
            {
                // The first Schedule of a process otherwise creates the job's reflection data inside the first
                // draw's timing window (SkiPisteMapSystem / SkiRunDrawFeedbackSystem.PrewarmJit, the same call).
                IJobExtensions.EarlyJobInit<RunOutlineJob>();
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"{nameof(SkiRunOutlineSystem)}: EarlyJobInit<RunOutlineJob> failed ({e.GetType().Name}: " +
                             $"{e.Message}) — the first draw pays the job setup instead (logged apart)");
            }
            Mod.Log.Info($"{nameof(SkiRunOutlineSystem)} created (our run outline overlay; it never writes the run " +
                         $"prefabs' snap distance; flag mirrors {(m_FlagsMatch ? "match" : "DIFFER — off")})");
        }

        /// <summary>RunOutlineRules' TempFlags / AreaFlags constants against the game's enums (RunOutlineRules
        /// engine notes FLAG MIRRORS).</summary>
        private static bool FlagMirrorsMatch(out string mismatch)
        {
            // Compared through Differs (a call, not a constant expression) so the compiler folds nothing.
            mismatch = Differs("TempFlags.Create", (uint)TempFlags.Create, RunOutlineRules.TempCreate)
                    ?? Differs("TempFlags.Delete", (uint)TempFlags.Delete, RunOutlineRules.TempDelete)
                    ?? Differs("TempFlags.Select", (uint)TempFlags.Select, RunOutlineRules.TempSelect)
                    ?? Differs("TempFlags.Modify", (uint)TempFlags.Modify, RunOutlineRules.TempModify)
                    ?? Differs("TempFlags.Replace", (uint)TempFlags.Replace, RunOutlineRules.TempReplace)
                    ?? Differs("TempFlags.Hidden", (uint)TempFlags.Hidden, RunOutlineRules.TempHidden)
                    ?? Differs("TempFlags.Parent", (uint)TempFlags.Parent, RunOutlineRules.TempParent)
                    ?? Differs("AreaFlags.Complete", (byte)AreaFlags.Complete, RunOutlineRules.AreaComplete)
                    ?? Differs("AreaFlags.CounterClockwise", (byte)AreaFlags.CounterClockwise, RunOutlineRules.AreaCounterClockwise)
                    ?? Differs("AreaFlags.Slave", (byte)AreaFlags.Slave, RunOutlineRules.AreaSlave);
            return mismatch == null;
        }

        private static UnityEngine.Color ToColor(float4 c) => new UnityEngine.Color(c.x, c.y, c.z, c.w);

        private static string Differs(string name, uint game, uint ours)
        {
            return game == ours ? null : name;
        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            try
            {
                CompleteDraw();
            }
            catch (Exception e)
            {
                Fail(e);
            }
            m_Armed = false;
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Armed = false;
            m_Disabled = false;
            m_ErrorLogged = false;
            m_CostLogged = false;
            m_SlowDrawLogged = false;
            m_RunCount = 0;
            Draws = 0;
            LastDrawMs = MaxDrawMs = 0f;
            m_DrawMsSum = 0.0;
            m_CostDraws = 0;
            if (mode != GameMode.Game || !m_FlagsMatch)
                return;
            try
            {
                Arm();
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        /// <summary>Resolves our four run prefabs, reads (never writes) their snap distance, logs ARMED.</summary>
        private void Arm()
        {
            float game = AreaUtils.GetMinNodeDistance(AreaType.Surface);   // AreaUtils.cs:410-421: 0.75
            float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
            string missing = null, differ = null;
            foreach (string name in RunSurfaceNames)
            {
                Entity prefab = Resolve(name);
                if (prefab == Entity.Null)
                {
                    missing = missing == null ? name : missing + ", " + name;
                    continue;
                }
                m_Runs[m_RunCount++] = prefab;
                if (!EntityManager.TryGetComponent(prefab, out AreaGeometryData geometry))
                {
                    differ = (differ == null ? "" : differ + ", ") + name + " (no AreaGeometryData)";
                    continue;
                }
                float v = geometry.m_SnapDistance;
                lo = math.min(lo, v);
                hi = math.max(hi, v);
                if (v != game)
                    differ = (differ == null ? "" : differ + ", ") + name + " " + v.ToString("0.###", CultureInfo.InvariantCulture);
            }
            for (int k = m_RunCount; k < m_Runs.Length; k++)
                m_Runs[k] = Entity.Null;
            m_Armed = true;

            int size = CurrentSize();
            m_DrawnSize = size;
            string snap = m_RunCount == 0 || float.IsInfinity(lo) ? "-"
                : lo == hi ? lo.ToString("0.###", CultureInfo.InvariantCulture)
                : lo.ToString("0.###", CultureInfo.InvariantCulture) + "-" + hi.ToString("0.###", CultureInfo.InvariantCulture);
            string halfSnap = m_RunCount == 0 || float.IsInfinity(lo) ? "-" : (lo * 0.5f).ToString("0.###", CultureInfo.InvariantCulture);
            string ours = RunOutlineRules.IsOn(size)
                ? $"our outline at size {size}: line {RunOutlineRules.LineWidth(size):0.0#} m, dots {RunOutlineRules.DotDiameter(size):0.0#} m white with a {RunOutlineRules.NodeRimWidth(size):0.0#} m dark rim"
                : "our outline off at size 0 (the game's thin outline only)";
            Mod.Log.Info($"RunOutline: ARMED — {ours}; run prefabs keep the game's snap distance {snap} (snapping {snap} m, " +
                         $"corners {halfSnap} m apart); {m_RunCount} of {RunSurfaceNames.Length} run prefab(s); the game's " +
                         "outline set (Temp Create/Delete/Select/Modify/Replace/Parent, or Error / Warning), its colors, " +
                         "its dropped shared sides and one dot per corner, projected on the terrain, Rendering before " +
                         "OverlayRenderSystem; setting read every frame; idle = one emptiness check");
            if (missing != null)
                Mod.Log.Warn($"RunOutline: run prefab(s) not found: {missing} — they keep only the game's thin outline " +
                             "(warned once per load)");
            if (differ != null)
                Mod.Log.Warn($"RunOutline: run prefab snap distance not the game's Surface value {game.ToString("0.###", CultureInfo.InvariantCulture)} " +
                             $"(AreaUtils.GetMinNodeDistance): {differ} — not ours (we never write it); left as is, so snapping " +
                             "and the corner rule follow it (warned once per load)");
        }

        protected override void OnUpdate()
        {
            if (!m_Armed || m_Disabled)
                return;
            try
            {
                // THE GUARD (engine notes THE FRAME).
                int size = CurrentSize();
                if (m_RunCount == 0)
                    return;
                if (size == 0)
                {
                    // A switch to 0 logs too, at the first frame that would have drawn. Once logged, m_DrawnSize is 0 and this is one int
                    // test per frame.
                    if (m_DrawnSize != 0 && RunOutlineRules.SizeChangeDue(m_DrawnSize, size, !m_SurfaceQuery.IsEmptyIgnoreFilter))
                    {
                        Mod.Log.Info($"RunOutline: size {m_DrawnSize} -> 0 (our outline off; the game's thin outline only)");
                        m_DrawnSize = 0;
                    }
                    return;
                }
                if (m_SurfaceQuery.IsEmptyIgnoreFilter)
                    return;
                Draw(size);
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        private static int CurrentSize()
        {
            SkiResortSettings settings = Mod.Settings;
            return RunOutlineRules.ClampSize(settings != null ? settings.RunOutlineSize : SkiResortSettings.DefaultRunOutlineSize);
        }

        private void Draw(int size)
        {
            long t0 = Stopwatch.GetTimestamp();
            // AreaBorderRenderSystem.cs:666-680: the game's fallbacks, then the singleton's four colors.
            var hovered = new UnityEngine.Color(0.5f, 0.5f, 1f, 0.5f);
            var error = new UnityEngine.Color(1f, 0.25f, 0.25f, 0.5f);
            var warning = new UnityEngine.Color(1f, 1f, 0.25f, 0.5f);
            var owner = new UnityEngine.Color(0.5f, 1f, 0.5f, 0.5f);
            if (!m_RenderingSettingsQuery.IsEmptyIgnoreFilter)
            {
                RenderingSettingsData s = m_RenderingSettingsQuery.GetSingleton<RenderingSettingsData>();
                hovered = s.m_HoveredColor;
                error = s.m_ErrorColor;
                warning = s.m_WarningColor;
                owner = s.m_OwnerColor;
            }
            m_AreaType.Update(this);
            m_TempType.Update(this);
            m_ErrorType.Update(this);
            m_WarningType.Update(this);
            m_PrefabRefType.Update(this);
            m_NodeType.Update(this);
            m_GeometryData.Update(this);
            NativeList<ArchetypeChunk> chunks = m_BorderQuery.ToArchetypeChunkListAsync(Allocator.TempJob, out JobHandle chunksHandle);
            NativeList<ArchetypeChunk> runChunks = m_RunAreaQuery.ToArchetypeChunkListAsync(Allocator.TempJob, out JobHandle runChunksHandle);
            OverlayRenderSystem.Buffer buffer = m_Overlay.GetBuffer(out JobHandle overlayDeps);   // OverlayRenderSystem.cs:603-634
            var job = new RunOutlineJob
            {
                AreaType = m_AreaType,
                TempType = m_TempType,
                ErrorType = m_ErrorType,
                WarningType = m_WarningType,
                PrefabRefType = m_PrefabRefType,
                NodeType = m_NodeType,
                GeometryData = m_GeometryData,
                Chunks = chunks,
                RunChunks = runChunks,
                Run0 = m_Runs[0],
                Run1 = m_Runs[1],
                Run2 = m_Runs[2],
                Run3 = m_Runs[3],
                Width = RunOutlineRules.LineWidth(size),
                Diameter = RunOutlineRules.DotDiameter(size),
                HoveredColour = hovered,
                OwnerColour = owner,
                WarningColour = warning,
                ErrorColour = error,
                // White with a thin dark rim, opaque (the game's dots, :408, are 50 % white and stay underneath).
                DotFill = ToColor(RunOutlineRules.NodeDotFill),
                DotRim = ToColor(RunOutlineRules.NodeDotRim),
                RimWidth = RunOutlineRules.NodeRimWidth(size),
                Overlay = buffer,
            };
            // AreaBorderRenderSystem.cs:705-708: the job runs after the ECS writers of what it reads, the chunk list and every
            // earlier overlay writer. It is registered with the overlay and handed back as this system's dependency.
            JobHandle handle = job.Schedule(JobHandle.CombineDependencies(Dependency, JobHandle.CombineDependencies(chunksHandle, runChunksHandle), overlayDeps));
            chunks.Dispose(handle);
            runChunks.Dispose(handle);
            m_Overlay.AddBufferWriter(handle);   // OverlayRenderSystem.cs:636-639. The overlay completes it at :738-741
            Dependency = handle;
            m_DrawHandle = handle;

            float ms = (float)((Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency);
            Draws++;
            LastDrawMs = ms;
            if (RunOutlineRules.SizeChangeDue(m_DrawnSize, size, wouldDraw: true))
            {
                // One line per change, at the next draw (a slider drag in Options logs once). OnUpdate logs a switch to 0.
                Mod.Log.Info($"RunOutline: size {m_DrawnSize} -> {size} (line {RunOutlineRules.LineWidth(size):0.0#} m, dots " +
                             $"{RunOutlineRules.DotDiameter(size):0.0#} m; snapping and the corner rule are the game's)");
                m_DrawnSize = size;
            }
            if (!s_FirstDrawDone)
            {
                // The first draw of the PROCESS pays the one-time JIT of Schedule<RunOutlineJob>. The system reports it on its own and
                // keeps it out of the peak, the mean and the slow-draw WARN.
                s_FirstDrawDone = true;
                Mod.Log.Info($"RunOutline: first overlay draw of this process {ms:F2} ms (one-time first call of the " +
                             "draw-job schedule, kept out of the draw peak; logged once per process)");
                return;
            }
            if (ms > MaxDrawMs)
                MaxDrawMs = ms;
            m_DrawMsSum += ms;
            m_CostDraws++;
            if (ms > Tuning.SlowDrawWarnMs && !m_SlowDrawLogged)
            {
                m_SlowDrawLogged = true;
                Mod.Log.Warn($"RunOutline [cost]: one draw frame took {ms:F2} ms on the main thread (> {Tuning.SlowDrawWarnMs:0.0} ms) " +
                             $"at size {size} (a frame with a Temp or flagged surface, ours or not; once per load)");
            }
            if (!m_CostLogged && Draws >= Tuning.CostLogAfterDraws)
            {
                m_CostLogged = true;
                double mean = m_CostDraws > 0 ? m_DrawMsSum / m_CostDraws : 0.0;
                Mod.Log.Info($"RunOutline [cost] first {Tuning.CostLogAfterDraws} draw frames with a Temp or flagged surface this " +
                             $"load (ours or not): main thread mean {mean:F3} ms, max {MaxDrawMs:F3} ms per frame (colors + chunk " +
                             "list + GetBuffer + schedule + AddBufferWriter; the outline itself is built on a worker, which " +
                             "returns at once when no area of ours is in the frame)");
            }
        }

        protected override void OnStopRunning()
        {
            try
            {
                CompleteDraw();
            }
            catch (Exception e)
            {
                Fail(e);
            }
            base.OnStopRunning();
        }

        private void CompleteDraw()
        {
            m_DrawHandle.Complete();
            m_DrawHandle = default;
        }

        private void Fail(Exception e)
        {
            m_Disabled = true;
            if (!m_ErrorLogged)
            {
                m_ErrorLogged = true;
                Mod.Log.Error($"!!! {nameof(SkiRunOutlineSystem)} EXCEPTION — our run outline is off until the next game " +
                              $"load (the game's thin outline remains). Please report this stack:\n{e}");
            }
        }

        /// <summary>House pattern (SkiRunOwnershipSystem.ResolveSet): fast PrefabID path + name-scan
        /// fallback. Returns Entity.Null when missing (pre-import states must not throw).</summary>
        private Entity Resolve(string name)
        {
            Entity prefabEntity = Entity.Null;
            if (m_PrefabSystem.TryGetPrefab(new PrefabID("SurfacePrefab", name), out PrefabBase prefabBase))
                m_PrefabSystem.TryGetEntity(prefabBase, out prefabEntity);
            if (prefabEntity != Entity.Null)
                return prefabEntity;

            var candidates = m_PrefabScanQuery.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < candidates.Length; i++)
                {
                    if (m_PrefabSystem.GetPrefabName(candidates[i]) == name)
                        return candidates[i];
                }
            }
            finally
            {
                candidates.Dispose();
            }
            return Entity.Null;
        }
    }
}
