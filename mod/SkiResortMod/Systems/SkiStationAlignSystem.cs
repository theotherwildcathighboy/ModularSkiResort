// SkiStationAlignSystem — the station alignment aid: subject, partner, overlay guide lines and the
// placement log (docs/research/hub-u2-spec.md §5.5; OD-9 (a): READ-ONLY guide + readout + an
// "aligned" state at 0.5 m / 0.5°; DECISIONS 130 "orientation/guidelines for when a top station is
// perfectly 180"). Created 2026-10-03 (Round L1, implementer I-2). The math is the pure
// StationAlign; the readout tooltip is SkiLiftTooltipSystem, which reads this system's state.
//
// READ-ONLY BY DESIGN. Snapping is not possible inside the vanilla object tool: its rotation is a
// private struct in a private NativeReference (Game.Tools/ObjectToolSystem.cs:54-63, :2275) with no
// public setter, and its only alignment snap is ExistingGeometry onto a net node (from :1025, the
// line-first mechanism).
// This system never writes to a tool, a station or a definition; it draws overlay lines and logs.
//
// SUBJECT (only while Settings ▸ Lifts ▸ LiftBuildAids is on), first match wins:
//   (i)  the vanilla object tool is the active tool (ToolSystem.activeTool, ToolSystem.cs:84) with a
//        station prefab (ObjectToolSystem.prefab, ObjectToolSystem.cs:2396) in Create mode
//        (actualMode, :2323; Mode enum :35-44) — or Move mode, the vanilla relocate (a deviation:
//        the spec names Create only; relocating is the same question) — subject = the Temp station
//        entity (the snapped preview) and its Transform [likely: Temp station previews carry
//        Transform every frame, spec §7.2 item 9; field step 3]. Placing with no preview yet
//        (cursor off terrain) = no subject; (ii)/(iii) are NOT consulted while placing;
//   (ii) else LiftCensusSystem.LastMovedStation if it moved within MovedWindowS of wall-clock time
//        (Move It nudges, connected or not — the reconnect state exists only with a line attached,
//        LiftReconnectSystem's "foreign edge" rule) — published as SubjectIsMoved (E1 fixer pass 2,
//        e1-spec §8.10 G1: only this subject's rows reach the mouse under a non-default tool,
//        StationAlign header READOUT PLACE);
//   (iii) else ToolSystem.selected (ToolSystem.cs:101) when it is a station.
// PARTNER: if the subject (a real station) is a terminal of a census row with both stations, the
// opposite station of that row (the field case: an already connected top station nudged with Move
// It). The same for a Move-mode preview through the station it relocates, Temp.m_Original (fixer
// round, finding l1-lifts-2: the relocated top station of a complete lift was measured against
// nothing, or an unrelated free base, because the free-station rule below skips stations on a
// Complete row — its own base included). Else a station of the OPPOSITE prefab kind, not the subject nor its Temp.m_Original, on no
// Complete census row, within PartnerRadiusM (planning constant, unverified) and with along > 0 seen
// from the candidate; smallest bearing error first, then the nearest. The pick is kept unless
// another candidate is better by SwitchMarginDeg for SwitchFrames consecutive frames (tunable).
// Partners are measured from LIVE Transforms (the census station list may lag a moving station by
// up to LiftCensusSystem.CoalesceFrames).
//
// DRAW (Rendering, anchored before OverlayRenderSystem — the SkiRunDrawFeedbackSystem anchor; the
// overlay lists are consumed and cleared by OverlayRenderSystem.OnUpdate later in the same phase,
// OverlayRenderSystem.cs:781, :790 per Mod.cs:349-356): the partner's axis DASHED from its stub tip
// to max(along + PartnerAxisExtraM, PartnerAxisMinM); the subject's axis SOLID SubjectAxisM; a
// perpendicular tick from the subject tip to its foot on the partner's axis. Colour: neutral, amber
// when within NearOffsetM and NearYawDeg (and in front), green when aligned. Lines are Projected
// (draped on the terrain, OverlayRenderSystem.StyleFlags.Projected = 2, :99-104). Buffer use is the
// main-thread pattern of SkiGladeToolSystem: GetBuffer(out deps) then deps.Complete() before writing
// (GetBuffer :603-634; DrawLine :207, DrawDashedLine :218; identical lines in 1.6.2).
//
// LOGS (while the aid is on): one line per station Created — `Align: placed SkiTopStationBuilding
// 119398:67 612 m from base 119459:77 — offset 0.2 m right, yaw 0.1° (aligned)` (capped at
// PlacementLinesPerFrame per frame + one summary line: a mass placement must not flood); one line
// per MOVE EPISODE (Round F6, item B3: it was one line per 15-frame pause, 18 lines in 3.7 s for one
// top station whose height re-settled after placement, findings/log-audit.md A5) — the pure
// StationMoveEpisodes (StationAlign.cs, header MOVE EPISODES) keeps one episode per station and
// closes it after StationMoveEpisodes.QuietSeconds of wall clock without a move of that station:
// `Align: moved and settled — moved 18 time(s) over 3.7 s; now SkiTopStationBuilding 119398:67 612 m
// from its lift's base …` (the final numbers, measured when the episode closes; a station removed
// meanwhile reads "(gone)" instead of staying silent). Created tags are visible at Rendering: they
// live until Cleanup (PrepareCleanUpSystem is the last MainLoop system, SystemOrder.cs:50, after
// RenderingSystem :68). Frame counts here are UnityEngine.Time.frameCount (advances while paused).
// The log verdict uses StationAlign.Classify (F6 B2): "aligned", "facing away from it — turn it
// around", "the <kind> faces away from it", else the tolerances.
//
// REGISTRATION (I-4, Mod.cs): updateSystem.UpdateBefore<SkiStationAlignSystem,
// Game.Rendering.OverlayRenderSystem>(SystemUpdatePhase.Rendering).
//
// CATALOG PHASE B (2026-10-11, docs/research/art/assets-2026-10/code-round-2/round-2-contract.md §6.4, lift group): the
// partner must be a station of the SUBJECT'S LIFT TYPE (StationDef.LiftTypeIndex through LiftCensusSystem.TryGetStationDef)
// — a row partner of another type (a mixed row) is not used, and a free candidate of another type is never picked: a
// detachable top is aligned with a detachable base only. Every stub tip is the station row's own
// (StationAlign.StubTip(pos, rot, StationDef.StubTipLocalZ): −16 on the detachable / gondola stations), for the subject,
// the partner, the candidates and the placement / settle log lines; SkiLiftTooltipSystem's readout reads SubjectTip /
// PartnerTip from here, so it follows. A fixed-grip-only city measures exactly as before (−12, one type).
//
// HOUSE RULES: "created" line; one `Align: ARMED` line per load with every constant; Game mode
// only; m_Armed / m_Disabled / m_ErrorLogged per load; one-shot `!!!` catch that disables the aid
// until the next load. No per-frame managed allocation: queries are probed with IsEmptyIgnoreFilter,
// chunk arrays are Allocator.Temp, strings are built only for log lines.

using System;
using Colossal.Entities;
using Colossal.Mathematics;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Rendering;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Transform = Game.Objects.Transform;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiStationAlignSystem : GameSystemBase
    {
        // ── constants (printed in the ARM line) ──────────────────────────────────────────────────
        internal const float PartnerRadiusM = 3000f;       // planning constant, unverified (spec §5.5)
        internal const float MovedWindowS = 2f;            // tunable
        internal const float SwitchMarginDeg = 5f;         // tunable
        internal const int SwitchFrames = 10;              // tunable
        internal const float SubjectAxisM = 60f;
        internal const float PartnerAxisExtraM = 60f;
        internal const float PartnerAxisMinM = 200f;
        internal const float NearOffsetM = 5f;             // amber band (tunable)
        internal const float NearYawDeg = 5f;              // amber band (tunable)
        internal const float AxisWidthM = 1.0f;
        internal const float TickWidthM = 0.6f;
        internal const float DashM = 6f, GapM = 4f;
        internal const int PlacementLinesPerFrame = 4;

        private static readonly UnityEngine.Color NeutralColor = new UnityEngine.Color(1f, 1f, 1f, 0.6f);
        private static readonly UnityEngine.Color AmberColor = new UnityEngine.Color(1f, 0.72f, 0.1f, 0.85f);
        private static readonly UnityEngine.Color GreenColor = new UnityEngine.Color(0.25f, 1f, 0.35f, 0.9f);

        // ── systems / queries ─────────────────────────────────────────────────────────────────────
        private ToolSystem m_ToolSystem;
        private ObjectToolSystem m_ObjectTool;
        private OverlayRenderSystem m_Overlay;
        private PrefabSystem m_PrefabSystem;
        private LiftCensusSystem m_Census;
        private EntityQuery m_TempStationQuery;
        private EntityQuery m_CreatedStationQuery;

        // ── state read by SkiLiftTooltipSystem (previous frame's values at UITooltip) ─────────────
        internal bool Active { get; private set; }
        internal bool HasPartner { get; private set; }
        /// <summary>Fixer round (l1-lifts-4): a free station of the other kind exists (in front or not).</summary>
        internal bool OtherKindExists { get; private set; }
        /// <summary>Fixer round (l1-lifts-4): the subject is a base station (prefab kind).</summary>
        internal bool SubjectIsBase { get; private set; }
        internal bool SubjectIsTemp { get; private set; }
        /// <summary>E1 fixer pass 2 (e1-spec §8.10 G1): the subject came from rule (ii), a station moved
        /// within MovedWindowS (a Move It nudge) — not from rule (i) or (iii). The tooltip's
        /// StationAlign.PlaceOf input.</summary>
        internal bool SubjectIsMoved { get; private set; }
        internal Entity Subject { get; private set; }
        internal Entity Partner => m_Partner;
        internal bool PartnerIsBase { get; private set; }
        internal float3 SubjectTip { get; private set; }
        internal float3 PartnerTip { get; private set; }
        internal StationAlign.Result Last { get; private set; }

        // ── private state ─────────────────────────────────────────────────────────────────────────
        private Entity m_Partner, m_Challenger;
        private int m_ChallengerFrames;
        private int m_SubjectType = -1;   // Catalog Phase B: the subject's StationDef.LiftTypeIndex (partners share it)
        private long m_SubjectKey = long.MinValue;
        private float2 m_SubjectAxis, m_PartnerAxis;
        private ObjectPrefab m_LastToolPrefab;
        private Entity m_LastToolPrefabEntity;
        private readonly StationMoveEpisodes m_MoveEpisodes = new StationMoveEpisodes();   // F6 B3
        private bool m_Armed, m_Disabled, m_ErrorLogged;

        /// <summary>Dev counters (this load). MovesLogged counts move EPISODES (F6 B3).</summary>
        public long FramesDrawn, PlacementsLogged, MovesLogged;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_ObjectTool = World.GetOrCreateSystemManaged<ObjectToolSystem>();
            m_Overlay = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_Census = World.GetOrCreateSystemManaged<LiftCensusSystem>();

            m_TempStationQuery = GetEntityQuery(
                ComponentType.ReadOnly<Game.Buildings.Building>(),
                ComponentType.ReadOnly<Temp>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadOnly<Transform>(),
                ComponentType.Exclude<Deleted>());
            m_CreatedStationQuery = GetEntityQuery(
                ComponentType.ReadOnly<Game.Buildings.Building>(),
                ComponentType.ReadOnly<Created>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadOnly<Transform>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());

            Mod.Log.Info($"{nameof(SkiStationAlignSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Armed = mode == GameMode.Game;
            m_Disabled = false;
            m_ErrorLogged = false;
            ResetSubject();
            m_MoveEpisodes.Reset();   // an open episode names an entity of the previous world: dropped
            m_LastToolPrefab = null;
            m_LastToolPrefabEntity = Entity.Null;
            FramesDrawn = PlacementsLogged = MovesLogged = 0;
            if (!m_Armed)
                return;
            Mod.Log.Info($"Align: ARMED (Lift building aids {(AidsOn ? "on" : "OFF")}) — subject: the station preview " +
                         $"of the object tool (Create/Move), else a station moved within {MovedWindowS} s, else the selected " +
                         $"station; partner (Catalog Phase B: of the subject's lift type; stub tips per station row): its lift's " +
                         $"other station, else the opposite kind within {PartnerRadiusM} m in " +
                         $"front, best bearing then nearest, switch only {SwitchMarginDeg}° better for {SwitchFrames} frames; " +
                         $"aligned |offset| ≤ {StationAlign.AlignedOffsetM} m, |yaw| ≤ {StationAlign.AlignedYawDeg}°, amber " +
                         $"≤ {NearOffsetM} m / {NearYawDeg}°; guide: partner axis dashed to max(along + {PartnerAxisExtraM}, " +
                         $"{PartnerAxisMinM}) m, subject axis {SubjectAxisM} m, widths {AxisWidthM}/{TickWidthM} m, dash " +
                         $"{DashM}/{GapM} m; readout: 'facing away' beyond {StationAlign.FacingAwayDeg}° yaw, 'the other " +
                         "station faces away' when behind its stub; logs: placed (≤ " +
                         $"{PlacementLinesPerFrame}/frame), moved and settled once per station move episode (closes " +
                         $"after {StationMoveEpisodes.QuietSeconds} s and {StationMoveEpisodes.MoveGapFrames} frames without " +
                         $"a move of that station; moves ≥ {StationMoveEpisodes.MoveGapFrames} frames apart count " +
                         "separately); read-only (no snapping)");
        }

        private static bool AidsOn => Mod.Settings == null || Mod.Settings.LiftBuildAids;

        protected override void OnUpdate()
        {
            try
            {
                if (!m_Armed || m_Disabled)
                    return;
                if (!AidsOn || !m_Census.IsArmed)
                {
                    if (Active)
                        ResetSubject();
                    return;
                }

                LogPlacements();
                LogSettledMove();

                if (!FindSubject(out Entity subject, out bool isTemp, out bool isMoved, out Entity original,
                        out bool subjectIsBase, out Transform subjectTransform, out long key, out StationDef subjectDef))
                {
                    if (Active)
                        ResetSubject();
                    return;
                }
                if (key != m_SubjectKey)
                {
                    m_SubjectKey = key;
                    m_Partner = m_Challenger = Entity.Null;
                    m_ChallengerFrames = 0;
                }
                Active = true;
                Subject = subject;
                SubjectIsTemp = isTemp;
                SubjectIsMoved = isMoved;
                SubjectIsBase = subjectIsBase;
                // Catalog Phase B (round-2-contract §6.4): the subject's lift type and its row's stub tip.
                m_SubjectType = subjectDef != null ? subjectDef.LiftTypeIndex : -1;
                float3 tip = StationAlign.StubTip(subjectTransform.m_Position, subjectTransform.m_Rotation, StubZ(subjectDef), StubX(subjectDef));
                m_SubjectAxis = StationAlign.Axis(subjectTransform.m_Rotation);
                SubjectTip = tip;

                // Header PARTNER: a real station uses its row's other station; so does a Move-mode
                // preview, through the station being relocated (Temp.m_Original; fixer round, finding
                // l1-lifts-2). A Create-mode preview has no original and takes the free-station rule.
                bool found = isTemp
                    ? (original != Entity.Null && TryRowPartner(original)) ||
                      PickCandidate(subject, original, subjectIsBase, tip, m_SubjectAxis)
                    : PickRowPartnerOrCandidate(subject, subjectIsBase, tip, m_SubjectAxis);
                HasPartner = found;
                if (!found)
                    return;

                Last = StationAlign.Measure(PartnerTip, m_PartnerAxis, SubjectTip, m_SubjectAxis);
                Draw();
                FramesDrawn++;
            }
            catch (Exception e)
            {
                m_Disabled = true;
                ResetSubject();
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiStationAlignSystem)} EXCEPTION — station alignment aid disabled until " +
                                  $"the next game load. Please report this stack:\n{e}");
                }
            }
        }

        private void ResetSubject()
        {
            Active = false;
            HasPartner = false;
            OtherKindExists = false;
            SubjectIsBase = false;
            SubjectIsTemp = false;
            SubjectIsMoved = false;
            Subject = Entity.Null;
            m_Partner = m_Challenger = Entity.Null;
            m_ChallengerFrames = 0;
            m_SubjectKey = long.MinValue;
            m_SubjectType = -1;
            Last = default;
        }

        /// <summary>Catalog Phase B: the row's stub tip z (StationAlign.StubTipLocalZ, the fixed-grip −12, without a row).</summary>
        private static float StubZ(StationDef def)
        {
            return def != null ? def.StubTipLocalZ : StationAlign.StubTipLocalZ;
        }

        /// <summary>Nitpick B2 (2026-10-13): the row's line axis x (StationDef.StubTipLocalX: −4.00 on the tram base's 6 x 3
        /// lot), 0 without a row.</summary>
        private static float StubX(StationDef def)
        {
            return def != null ? def.StubTipLocalX : 0f;
        }

        /// <summary>Catalog Phase B: a station row of the subject's lift type (both unknown counts as the same).</summary>
        private bool SameType(StationDef def)
        {
            return (def != null ? def.LiftTypeIndex : -1) == m_SubjectType;
        }

        // ════════════════════════════════════════════════════════════════════════════════════════
        // Subject
        // ════════════════════════════════════════════════════════════════════════════════════════

        private bool FindSubject(out Entity subject, out bool isTemp, out bool isMoved, out Entity original,
            out bool isBase, out Transform transform, out long key, out StationDef def)
        {
            subject = original = Entity.Null;
            isTemp = isMoved = isBase = false;
            transform = default;
            key = 0;
            def = null;

            // (i) placing / relocating a station with the vanilla object tool.
            if (m_ToolSystem.activeTool == m_ObjectTool)
            {
                Entity toolPrefab = ToolPrefabEntity();
                ObjectToolSystem.Mode mode = m_ObjectTool.actualMode;
                if (toolPrefab != Entity.Null && m_Census.TryGetStationKind(toolPrefab, out isBase) &&
                    (mode == ObjectToolSystem.Mode.Create || mode == ObjectToolSystem.Mode.Move))
                {
                    if (!FindTempStation(toolPrefab, out subject, out original, out transform))
                        return false;   // placing, no preview this frame: no subject at all
                    m_Census.TryGetStationDef(toolPrefab, out def);   // Catalog Phase B: the preview's row (type, stub z)
                    isTemp = true;
                    key = ((long)toolPrefab.Index << 32) ^ (uint)original.Index ^ 0x5A5A0000L;
                    return true;
                }
            }

            // (ii) a station that moved within the window (Move It nudges, relocations).
            Entity moved = m_Census.LastMovedStation;
            if (moved != Entity.Null &&
                UnityEngine.Time.realtimeSinceStartup - m_Census.LastMovedRealtime <= MovedWindowS &&
                TryRealStation(moved, out isBase, out transform, out def))
            {
                subject = moved;
                isMoved = true;   // E1 fixer pass 2: the tooltip shows this one on the mouse under any tool
                key = ((long)moved.Index << 32) | (uint)moved.Version;
                return true;
            }

            // (iii) the selected station.
            Entity selected = m_ToolSystem.selected;
            if (selected != Entity.Null && TryRealStation(selected, out isBase, out transform, out def))
            {
                subject = selected;
                key = ((long)selected.Index << 32) | (uint)selected.Version;
                return true;
            }
            return false;
        }

        private Entity ToolPrefabEntity()
        {
            ObjectPrefab prefab = m_ObjectTool.prefab;
            if (!ReferenceEquals(prefab, m_LastToolPrefab))
            {
                m_LastToolPrefab = prefab;
                m_LastToolPrefabEntity = Entity.Null;
                if (prefab != null && m_PrefabSystem.TryGetEntity(prefab, out Entity e))
                    m_LastToolPrefabEntity = e;
            }
            return m_LastToolPrefabEntity;
        }

        private bool FindTempStation(Entity prefab, out Entity station, out Entity original, out Transform transform)
        {
            station = original = Entity.Null;
            transform = default;
            if (m_TempStationQuery.IsEmptyIgnoreFilter)
                return false;
            var chunks = m_TempStationQuery.ToArchetypeChunkArray(Allocator.Temp);
            try
            {
                var entityHandle = GetEntityTypeHandle();
                var prefabHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
                var tempHandle = GetComponentTypeHandle<Temp>(isReadOnly: true);
                var transformHandle = GetComponentTypeHandle<Transform>(isReadOnly: true);
                for (int c = 0; c < chunks.Length; c++)
                {
                    var prefabs = chunks[c].GetNativeArray(ref prefabHandle);
                    var temps = chunks[c].GetNativeArray(ref tempHandle);
                    for (int i = 0; i < prefabs.Length; i++)
                    {
                        if (prefabs[i].m_Prefab != prefab || (temps[i].m_Flags & TempFlags.Delete) != 0)
                            continue;
                        station = chunks[c].GetNativeArray(entityHandle)[i];
                        original = temps[i].m_Original;
                        transform = chunks[c].GetNativeArray(ref transformHandle)[i];
                        return true;
                    }
                }
            }
            finally
            {
                chunks.Dispose();
            }
            return false;
        }

        private bool TryRealStation(Entity e, out bool isBase, out Transform transform)
        {
            return TryRealStation(e, out isBase, out transform, out _);
        }

        /// <summary>Catalog Phase B: TryRealStation with the station's catalog row (lift type, stub tip z).</summary>
        private bool TryRealStation(Entity e, out bool isBase, out Transform transform, out StationDef def)
        {
            isBase = false;
            transform = default;
            def = null;
            return EntityManager.Exists(e) && !EntityManager.HasComponent<Deleted>(e) &&
                   !EntityManager.HasComponent<Temp>(e) &&
                   EntityManager.TryGetComponent<PrefabRef>(e, out var prefab) &&
                   m_Census.TryGetStationDef(prefab.m_Prefab, out def) &&
                   m_Census.TryGetStationKind(prefab.m_Prefab, out isBase) &&
                   EntityManager.TryGetComponent<Transform>(e, out transform);
        }

        // ════════════════════════════════════════════════════════════════════════════════════════
        // Partner
        // ════════════════════════════════════════════════════════════════════════════════════════

        private bool PickRowPartnerOrCandidate(Entity subject, bool subjectIsBase, float3 tip, float2 axis)
        {
            return TryRowPartner(subject) || PickCandidate(subject, Entity.Null, subjectIsBase, tip, axis);
        }

        /// <summary>The other station of <paramref name="station"/>'s census row (both stations alive),
        /// set as the partner. <paramref name="station"/> is a real station: the subject itself, or the
        /// station a Move-mode preview relocates (its Temp.m_Original — GenerateObjectsSystem.cs:989-992
        /// copies the definition's original into Temp; the Relocate definition's original is
        /// ObjectToolSystem.m_MovingObject, set by StartMoving :3020 and passed to CreateDefinitions
        /// :4353; 1.6.2 :3079, :4421).</summary>
        private bool TryRowPartner(Entity station)
        {
            // Catalog Phase B (round-2-contract §6.4): the row partner must share the subject's lift type (a mixed row's
            // other station is no partner: the free-station rule picks one of the right type instead).
            if (m_Census.TryGetOppositeStation(station, out Entity other) &&
                TryRealStation(other, out bool otherIsBase, out Transform t, out StationDef otherDef) && SameType(otherDef))
            {
                SetPartner(other, otherIsBase, t, otherDef);
                m_Challenger = Entity.Null;
                m_ChallengerFrames = 0;
                OtherKindExists = true;
                return true;
            }
            return false;
        }

        /// <summary>Best free station of the opposite kind (see header PARTNER), with hysteresis. Also sets
        /// OtherKindExists: a free station of the opposite kind exists at all, in front or not, near or
        /// far (fixer round, finding l1-lifts-4: the tooltip warns only then, or for a top station).</summary>
        private bool PickCandidate(Entity subject, Entity original, bool subjectIsBase, float3 tip, float2 axis)
        {
            Entity best = Entity.Null;
            float bestErr = float.MaxValue, bestDist = float.MaxValue, currentErr = float.MaxValue;
            bool currentValid = false;
            Transform bestT = default, currentT = default;
            bool bestIsBase = false, currentIsBase = false;
            StationDef bestDef = null, currentDef = null;
            OtherKindExists = false;

            int n = m_Census.StationCount;
            for (int i = 0; i < n; i++)
            {
                Entity e = m_Census.StationEntityAt(i);
                if (e == subject || e == original || m_Census.StationAt(i).IsBase == subjectIsBase)
                    continue;
                // Catalog Phase B (round-2-contract §6.4): only a station of the subject's lift type is a candidate.
                if (!TryRealStation(e, out bool eIsBase, out Transform t, out StationDef eDef) || eIsBase == subjectIsBase ||
                    !SameType(eDef))
                    continue;
                if (m_Census.IsOnCompleteRow(e))
                    continue;
                OtherKindExists = true;
                StationAlign.Result r = StationAlign.Measure(StationAlign.StubTip(t.m_Position, t.m_Rotation, StubZ(eDef), StubX(eDef)),
                    StationAlign.Axis(t.m_Rotation), tip, axis);
                if (!(r.AlongM > 0f) || r.DistanceM > PartnerRadiusM)
                    continue;
                if (e == m_Partner)
                {
                    currentValid = true;
                    currentErr = r.BearingErrorDeg;
                    currentT = t;
                    currentIsBase = eIsBase;
                    currentDef = eDef;
                }
                if (r.BearingErrorDeg < bestErr || (r.BearingErrorDeg == bestErr && r.DistanceM < bestDist))
                {
                    best = e;
                    bestErr = r.BearingErrorDeg;
                    bestDist = r.DistanceM;
                    bestT = t;
                    bestIsBase = eIsBase;
                    bestDef = eDef;
                }
            }

            if (best == Entity.Null)
            {
                m_Partner = m_Challenger = Entity.Null;
                m_ChallengerFrames = 0;
                return false;
            }
            if (!currentValid || best == m_Partner)
            {
                m_Challenger = Entity.Null;
                m_ChallengerFrames = 0;
                if (!currentValid)
                {
                    SetPartner(best, bestIsBase, bestT, bestDef);
                    return true;
                }
                SetPartner(m_Partner, currentIsBase, currentT, currentDef);
                return true;
            }

            // Keep the current pick unless the best is better by the margin for SwitchFrames frames.
            if (bestErr + SwitchMarginDeg <= currentErr)
            {
                if (best == m_Challenger) m_ChallengerFrames++;
                else { m_Challenger = best; m_ChallengerFrames = 1; }
                if (m_ChallengerFrames >= SwitchFrames)
                {
                    m_Challenger = Entity.Null;
                    m_ChallengerFrames = 0;
                    SetPartner(best, bestIsBase, bestT, bestDef);
                    return true;
                }
            }
            else
            {
                m_Challenger = Entity.Null;
                m_ChallengerFrames = 0;
            }
            SetPartner(m_Partner, currentIsBase, currentT, currentDef);
            return true;
        }

        private void SetPartner(Entity partner, bool isBase, Transform t, StationDef def)
        {
            m_Partner = partner;
            PartnerIsBase = isBase;
            // Catalog Phase B: the partner row's own stub tip (was StationAlign.StubTip(pos, rot), the fixed-grip −12).
            PartnerTip = StationAlign.StubTip(t.m_Position, t.m_Rotation, StubZ(def), StubX(def));
            m_PartnerAxis = StationAlign.Axis(t.m_Rotation);
        }

        // ════════════════════════════════════════════════════════════════════════════════════════
        // Draw
        // ════════════════════════════════════════════════════════════════════════════════════════

        private void Draw()
        {
            StationAlign.Result r = Last;
            UnityEngine.Color color = r.Aligned ? GreenColor
                : r.AlongM > 0f && math.abs(r.LateralM) <= NearOffsetM && math.abs(r.YawDeg) <= NearYawDeg ? AmberColor
                : NeutralColor;

            // Main-thread write: complete the existing writers first (OverlayRenderSystem.cs:603-634).
            OverlayRenderSystem.Buffer buffer = m_Overlay.GetBuffer(out JobHandle deps);
            deps.Complete();
            var flags = OverlayRenderSystem.StyleFlags.Projected;
            var round = new float2(1f, 1f);

            float3 pp = PartnerTip;
            float3 fp = new float3(m_PartnerAxis.x, 0f, m_PartnerAxis.y);
            float length = math.max(r.AlongM + PartnerAxisExtraM, PartnerAxisMinM);
            buffer.DrawDashedLine(color, color, 0f, flags, new Line3.Segment(pp, pp + fp * length),
                AxisWidthM, DashM, GapM);   // :218

            float3 ps = SubjectTip;
            float3 fs = new float3(m_SubjectAxis.x, 0f, m_SubjectAxis.y);
            if (math.lengthsq(fs) > 0.25f)
                buffer.DrawLine(color, color, 0f, flags, new Line3.Segment(ps, ps + fs * SubjectAxisM), AxisWidthM, round);   // :207

            if (r.AlongM > 0f && math.abs(r.LateralM) > 0.01f)
            {
                float3 foot = pp + fp * r.AlongM;
                foot.y = ps.y;
                buffer.DrawLine(color, color, 0f, flags, new Line3.Segment(ps, foot), TickWidthM, round);
            }
        }

        // ════════════════════════════════════════════════════════════════════════════════════════
        // Logs
        // ════════════════════════════════════════════════════════════════════════════════════════

        private void LogPlacements()
        {
            if (m_CreatedStationQuery.IsEmptyIgnoreFilter)
                return;
            int logged = 0, suppressed = 0;
            var chunks = m_CreatedStationQuery.ToArchetypeChunkArray(Allocator.Temp);
            try
            {
                var entityHandle = GetEntityTypeHandle();
                var prefabHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
                for (int c = 0; c < chunks.Length; c++)
                {
                    var entities = chunks[c].GetNativeArray(entityHandle);
                    var prefabs = chunks[c].GetNativeArray(ref prefabHandle);
                    for (int i = 0; i < entities.Length; i++)
                    {
                        if (!m_Census.TryGetStationKind(prefabs[i].m_Prefab, out _))
                            continue;
                        if (logged >= PlacementLinesPerFrame) { suppressed++; continue; }
                        logged++;
                        PlacementsLogged++;
                        Mod.Log.Info("Align: placed " + DescribeAlignment(entities[i]));
                    }
                }
            }
            finally
            {
                chunks.Dispose();
            }
            if (suppressed > 0)
                Mod.Log.Info($"Align: … and {suppressed} more station(s) placed this frame (lines capped at {PlacementLinesPerFrame})");
        }

        /// <summary>F6 B3 (header LOGS): feeds the census's last move (recorded at Modification4B of this
        /// frame; this system runs at Rendering) into the per-station episodes and writes ONE line per
        /// episode that has gone quiet. Per frame: one compare, and a walk over the open episodes (a
        /// station or two at most); strings only when a line is written.</summary>
        private void LogSettledMove()
        {
            Entity moved = m_Census.LastMovedStation;
            if (moved != Entity.Null)
                m_MoveEpisodes.Observe(new EntityRef(moved.Index, moved.Version), m_Census.LastMovedFrame,
                    m_Census.LastMovedRealtime);
            if (m_MoveEpisodes.OpenCount == 0)
                return;
            long frameNow = UnityEngine.Time.frameCount;
            double timeNow = UnityEngine.Time.realtimeSinceStartup;
            while (m_MoveEpisodes.TryCloseOne(frameNow, timeNow, out StationMoveEpisodes.Episode ep))
            {
                MovesLogged++;
                var station = new Entity { Index = ep.Station.Index, Version = ep.Station.Version };
                Mod.Log.Info($"Align: moved and settled — moved {ep.Moves} time(s) over {ep.SpanSeconds:F1} s; now " +
                             DescribeAlignment(station));
            }
        }

        /// <summary>"SkiTopStationBuilding 119398:67 612 m from base 119459:77 — offset 0.2 m right, yaw
        /// 0.1° (aligned)", measured with the same partner rule as the guide (log only: allocates).</summary>
        private string DescribeAlignment(Entity station)
        {
            if (!TryRealStation(station, out bool isBase, out Transform t, out StationDef stationDef))
                return $"station {station.Index}:{station.Version} (gone)";
            string name = EntityManager.TryGetComponent<PrefabRef>(station, out var prefab)
                ? m_PrefabSystem.GetPrefabName(prefab.m_Prefab) : "station";
            // Catalog Phase B (round-2-contract §6.4): the station row's stub tip; partners of the same lift type only.
            float3 tip = StationAlign.StubTip(t.m_Position, t.m_Rotation, StubZ(stationDef), StubX(stationDef));
            float2 axis = StationAlign.Axis(t.m_Rotation);
            int type = stationDef != null ? stationDef.LiftTypeIndex : -1;

            Entity partner = Entity.Null;
            bool partnerIsBase = false;
            Transform pt = default;
            StationDef partnerDef = null;
            string how = "its lift's";
            if (m_Census.TryGetOppositeStation(station, out Entity other) &&
                TryRealStation(other, out partnerIsBase, out pt, out partnerDef) &&
                (partnerDef != null ? partnerDef.LiftTypeIndex : -1) == type)
            {
                partner = other;
            }
            else
            {
                how = "the free";
                float bestErr = float.MaxValue, bestDist = float.MaxValue;
                for (int i = 0; i < m_Census.StationCount; i++)
                {
                    Entity e = m_Census.StationEntityAt(i);
                    if (e == station || !TryRealStation(e, out bool eb, out Transform et, out StationDef eDef) || eb == isBase ||
                        (eDef != null ? eDef.LiftTypeIndex : -1) != type || m_Census.IsOnCompleteRow(e))
                        continue;
                    var r = StationAlign.Measure(StationAlign.StubTip(et.m_Position, et.m_Rotation, StubZ(eDef), StubX(eDef)),
                        StationAlign.Axis(et.m_Rotation), tip, axis);
                    if (!(r.AlongM > 0f) || r.DistanceM > PartnerRadiusM)
                        continue;
                    if (r.BearingErrorDeg < bestErr || (r.BearingErrorDeg == bestErr && r.DistanceM < bestDist))
                    {
                        bestErr = r.BearingErrorDeg;
                        bestDist = r.DistanceM;
                        partner = e;
                        partnerIsBase = eb;
                        pt = et;
                        partnerDef = eDef;
                    }
                }
            }
            string kind = isBase ? "top" : "base";
            if (partner == Entity.Null)
                return $"{name} {station.Index}:{station.Version} — no free {kind} station within {PartnerRadiusM:F0} m in front of it";

            var m = StationAlign.Measure(StationAlign.StubTip(pt.m_Position, pt.m_Rotation, StubZ(partnerDef), StubX(partnerDef)),
                StationAlign.Axis(pt.m_Rotation), tip, axis);
            string side = math.abs(m.LateralM) < 0.05f ? string.Empty : m.LateralM > 0f ? " left" : " right";
            // F6 B2: the same classes as the readout (StationAlign header READOUT CLASS).
            StationAlign.Readout cls = StationAlign.Classify(m);
            string verdict = cls == StationAlign.Readout.Straight ? "aligned"
                : cls == StationAlign.Readout.FacingAway ? "not aligned: facing away from it — turn it around"
                : cls == StationAlign.Readout.PartnerFacingAway
                    ? $"not aligned: the {(partnerIsBase ? "base" : "top")} faces away from it (this station is behind its stub)"
                : $"not aligned: want ≤ {StationAlign.AlignedOffsetM} m and ≤ {StationAlign.AlignedYawDeg}°";
            return $"{name} {station.Index}:{station.Version} {m.DistanceM:F0} m from {how} {(partnerIsBase ? "base" : "top")} " +
                   $"{partner.Index}:{partner.Version} — offset {math.abs(m.LateralM):F1} m{side}, yaw {m.YawDeg:F1}°, " +
                   $"rise {m.RiseM:F0} m ({verdict})";
        }
    }
}
