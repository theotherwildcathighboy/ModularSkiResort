// SkiMergeOverlaySystem - the hub-armed merge overlay (P2.6; MergeRules "merge overlay" rules). While armed it outlines every building near
// a ski area that the merge card would take: green when a lodge tier fits its lot, gray when the kind qualifies but the lot is too small
// for every tier (MergeRules.TintOf). Buildings that can never be merged, and merged ones, are not drawn. A click selects a building the
// vanilla way and the merge card follows the selection (SkiResortHubUISystem.Merge.cs); this system selects nothing.
// "Near" is the merged buildings' slopeside rule over every area: within SkiDayRules.SlopesideMaxM of a lift station or a run outline
// (SkiResortAttractivenessSystem.NearAnySlopeside, the published geometry of the last recompute).
// Tint: OverlayRenderSystem.Buffer projected lines along the lot (BuildingUtils.CalculateCorners, BuildingUtils.cs:72-86), the
// SkiSnowLineOverlaySystem main-thread pattern: GetBuffer(out deps), deps.Complete() (OverlayRenderSystem.cs:603-634). No game component
// is written (vanilla Highlighted has one color and is a structural change per building).
// The candidate list is built when the mode arms, when the slopeside geometry moved, when the building count changed (polled every
// CountPollFrames) or after an unmerge (MergeRules.OverlayRebuild); drawing reads the cached corners only. One `Merge [cost] overlay` line
// per build. Disarms on the hub button, Escape, a merge or tier change, a load, and when the city has no Welcome Sign (MergeRules.NextArmed).
// Phase: Rendering, anchored UpdateBefore<SkiMergeOverlaySystem, OverlayRenderSystem> (Mod.cs; load-bearing like the snow line's: the
// renderer uploads and clears its lists in the same frame).
// Engine notes: EngineNotes/SkiMergeSystem.md (Overlay)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Colossal.Entities;
using Colossal.Mathematics;
using Colossal.Serialization.Entities;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Prefabs;
using Game.Rendering;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiMergeOverlaySystem : GameSystemBase
    {
        private const int CountPollFrames = 60;
        private const float LineWidthM = 2f;
        private const float SlowBuildWarnMs = 20f;

        // sRGB / 255 (OverlayRenderSystem converts with Color.linear).
        private static readonly UnityEngine.Color s_Allowed = new UnityEngine.Color(110f / 255f, 220f / 255f, 120f / 255f, 0.95f);
        private static readonly UnityEngine.Color s_TooSmall = new UnityEngine.Color(170f / 255f, 170f / 255f, 170f / 255f, 0.85f);

        private struct Candidate
        {
            public Entity Building;
            public Quad3 Corners;
            public MergeRules.OverlayTint Tint;
        }

        private SkiPrefabCatalogSystem m_PrefabCatalog;
        private SkiResortAttractivenessSystem m_Attractiveness;
        private OverlayRenderSystem m_Overlay;
        private EntityQuery m_BuildingQuery;
        private readonly List<Candidate> m_Candidates = new List<Candidate>(64);

        private bool m_Game, m_Disabled, m_ErrorLogged;
        private bool m_Armed;
        private int m_Request = -1;           // the hub button this frame: 1 arm, 0 disarm, -1 none
        private bool m_MergedPending, m_RebuildPending, m_LoadPending;
        private int m_BuiltGeometry = int.MinValue, m_BuiltCount = -1, m_Count = -1;
        private int m_PollTicks;

        /// <summary>The armed state (the hub's mergeOverlay binding).</summary>
        internal bool Armed => m_Armed;

        /// <summary>Candidates of the last build: green (a tier fits) and gray (lot too small).</summary>
        internal int AllowedCount { get; private set; }
        internal int TooSmallCount { get; private set; }

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabCatalog = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>();
            m_Attractiveness = World.GetOrCreateSystemManaged<SkiResortAttractivenessSystem>();
            m_Overlay = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
            m_BuildingQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Building>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<Game.Objects.Transform>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Owner>() },
            });
            Mod.Log.Info($"{nameof(SkiMergeOverlaySystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Game = mode == GameMode.Game;
            m_Disabled = m_ErrorLogged = false;
            m_LoadPending = true;   // MergeRules.NextArmed: a load always disarms
            m_Request = -1;
            m_MergedPending = m_RebuildPending = false;
            ClearList();
        }

        // Main-thread calls (the hub's trigger, SkiMergeSystem)

        /// <summary>The hub button: arm (true) or disarm (false), applied on the next frame.</summary>
        internal void RequestArmed(bool on) => m_Request = on ? 1 : 0;

        /// <summary>SkiMergeSystem after a merge or a tier change: disarm.</summary>
        internal void OnMerged() => m_MergedPending = true;

        /// <summary>SkiMergeSystem after an unmerge: the building may be a candidate again.</summary>
        internal void RequestRebuild() => m_RebuildPending = true;

        protected override void OnUpdate()
        {
            try
            {
                if (m_Disabled || !m_Game)
                    return;
                bool escape = m_Armed && EscapePressed();
                bool hasSign = ResortPresence.Signs > 0;
                bool was = m_Armed;
                m_Armed = MergeRules.NextArmed(m_Armed, m_Request, escape, m_MergedPending, m_LoadPending, hasSign);
                if (was != m_Armed || m_Request >= 0)
                    LogArmed(was, escape, hasSign);
                m_Request = -1;
                m_MergedPending = m_LoadPending = false;
                if (!m_Armed)
                {
                    if (was)
                        ClearList();
                    return;
                }
                if (--m_PollTicks <= 0 || !was)
                {
                    m_PollTicks = CountPollFrames;
                    m_Count = m_BuildingQuery.CalculateEntityCount();
                }
                if (MergeRules.OverlayRebuild(m_Armed, !was, m_Attractiveness.SlopesideRevision, m_BuiltGeometry, m_Count, m_BuiltCount, m_RebuildPending))
                    Build(!was ? "armed" : m_RebuildPending ? "unmerged" : m_Attractiveness.SlopesideRevision != m_BuiltGeometry ? "areas changed" : "buildings changed");
                Draw();
            }
            catch (Exception e)
            {
                m_Disabled = true;
                m_Armed = false;
                ClearList();
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiMergeOverlaySystem)} EXCEPTION - the merge overlay is off until the next game load. Please report this stack:\n{e}");
                }
            }
        }

        private static bool EscapePressed()
        {
            // The default tool overrides its cancel action with the right mouse button (DefaultToolSystem.cs:741), so Escape is read from
            // the keyboard. Escape also does its vanilla job (deselect, then the pause menu).
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            return keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
        }

        private void LogArmed(bool was, bool escape, bool hasSign)
        {
            if (m_Armed && !was)
                Mod.Log.Info("Merge [overlay] armed: green = a lodge tier fits the lot, gray = the lot is too small for every tier");
            else if (!m_Armed && was)
                Mod.Log.Info($"Merge [overlay] disarmed ({(m_LoadPending ? "load" : escape ? "Escape" : m_MergedPending ? "merged" : !hasSign ? "no Welcome Sign" : "hub button")})");
            else if (m_Request == 1 && !m_Armed)
                Mod.Log.Info("Merge [overlay] not armed: the city has no Welcome Sign");
        }

        private void ClearList()
        {
            m_Candidates.Clear();
            AllowedCount = TooSmallCount = 0;
            m_BuiltGeometry = int.MinValue;
            m_BuiltCount = -1;
            m_PollTicks = 0;
        }

        /// <summary>The candidate list: every top-level building near a ski area (bounds first, then the slopeside rule), classified like the
        /// merge card does (SkiMergeSystem.ReadFacts, MergeRules.Classify / AllowedMask / TintOf).</summary>
        private void Build(string reason)
        {
            var sw = Stopwatch.StartNew();
            m_RebuildPending = false;
            m_BuiltGeometry = m_Attractiveness.SlopesideRevision;
            m_BuiltCount = m_Count;
            m_Candidates.Clear();
            int allowed = 0, tooSmall = 0, near = 0, scanned = 0;
            if (m_Attractiveness.TryGetSlopesideBounds(out float2 min, out float2 max))
            {
                using (var entities = m_BuildingQuery.ToEntityArray(Allocator.Temp))
                using (var transforms = m_BuildingQuery.ToComponentDataArray<Game.Objects.Transform>(Allocator.Temp))
                {
                    scanned = entities.Length;
                    int tiers = SkiCatalog.Lodges.Count;
                    for (int i = 0; i < entities.Length; i++)
                    {
                        float2 xz = transforms[i].m_Position.xz;
                        if (math.any(xz < min) || math.any(xz > max) || !m_Attractiveness.NearAnySlopeside(xz))
                            continue;
                        near++;
                        Entity b = entities[i];
                        MergeRules.Facts facts = SkiMergeSystem.ReadFacts(EntityManager, m_PrefabCatalog, b, forTag: false);
                        MergeRules.Kind kind = MergeRules.Classify(facts, out MergeRules.Refusal refusal);
                        MergeRules.OverlayTint tint = MergeRules.TintOf(kind, refusal, MergeRules.AllowedMask(facts.LotCells, tiers),
                                                                        EntityManager.HasComponent<SkiMergedLodge>(b));
                        if (tint == MergeRules.OverlayTint.None ||
                            !EntityManager.TryGetComponent(b, out PrefabRef pr) || !EntityManager.TryGetComponent(pr.m_Prefab, out BuildingData data))
                            continue;
                        m_Candidates.Add(new Candidate { Building = b, Corners = BuildingUtils.CalculateCorners(transforms[i], data.m_LotSize), Tint = tint });
                        if (tint == MergeRules.OverlayTint.Allowed)
                            allowed++;
                        else
                            tooSmall++;
                    }
                }
            }
            AllowedCount = allowed;
            TooSmallCount = tooSmall;
            double ms = sw.Elapsed.TotalMilliseconds;
            var ci = CultureInfo.InvariantCulture;
            string line = $"Merge [cost] overlay ({reason}): {ms.ToString("F2", ci)} ms; {scanned} building(s) scanned, {near} near a ski area, " +
                          $"{allowed} green (a tier fits), {tooSmall} gray (lot too small)";
            if (ms > SlowBuildWarnMs)
                Mod.Log.Warn(line + $" - over {SlowBuildWarnMs.ToString("F0", ci)} ms");
            else
                Mod.Log.Info(line);
        }

        private void Draw()
        {
            if (m_Candidates.Count == 0)
                return;
            OverlayRenderSystem.Buffer buffer = m_Overlay.GetBuffer(out JobHandle deps);   // OverlayRenderSystem.cs:603-634
            deps.Complete();                                                               // main-thread write, no AddBufferWriter
            var flags = OverlayRenderSystem.StyleFlags.Projected;
            var round = new float2(1f, 1f);
            for (int i = 0; i < m_Candidates.Count; i++)
            {
                Candidate c = m_Candidates[i];
                UnityEngine.Color color = c.Tint == MergeRules.OverlayTint.Allowed ? s_Allowed : s_TooSmall;
                Quad3 q = c.Corners;
                buffer.DrawLine(color, color, 0f, flags, new Line3.Segment(q.a, q.b), LineWidthM, round);
                buffer.DrawLine(color, color, 0f, flags, new Line3.Segment(q.b, q.c), LineWidthM, round);
                buffer.DrawLine(color, color, 0f, flags, new Line3.Segment(q.c, q.d), LineWidthM, round);
                buffer.DrawLine(color, color, 0f, flags, new Line3.Segment(q.d, q.a), LineWidthM, round);
            }
        }
    }
}
