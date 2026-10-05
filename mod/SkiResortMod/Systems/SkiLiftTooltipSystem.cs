// SkiLiftTooltipSystem — the lift building aids' tooltips: the station alignment readout and the
// bulldozer warning (docs/research/hub-u2-spec.md §5.5 "Tooltip", §5.7; Settings ▸ Lifts ▸
// LiftBuildAids). Created 2026-10-03 (Round L1, implementer I-2).
//
// 1. ALIGNMENT READOUT (state from SkiStationAlignSystem, which runs at Rendering: TooltipUISystem is
//    a UIUpdate system and UIUpdate precedes Rendering in MainLoop, SystemOrder.cs:67-68, so this is
//    the PREVIOUS frame's measurement — one frame of lag, accepted). The rows, by StationAlign.Classify
//    (Round F6, item B2; StationAlign header READOUT CLASS):
//      Numbers           → ALIGN_OFFSET (FloatTooltip, unit "length", signed: + left / − right of the
//                          partner's axis seen from the partner, StationAlign SIGN CONVENTION),
//                          ALIGN_ANGLE (unit "angle", signed), ALIGN_DISTANCE (length), ALIGN_RISE
//                          (length, signed);
//      Straight          → ALIGN_STRAIGHT (StringTooltip, Success) + ALIGN_DISTANCE + ALIGN_RISE;
//      FacingAway        → ALIGN_OFFSET + ALIGN_FACING_AWAY (StringTooltip, Warning, in place of the
//                          angle: |yaw| > 90° read "−180 °" in field test 6, the same 180 as the green
//                          "Straight — 180°") + ALIGN_DISTANCE + ALIGN_RISE;
//      PartnerFacingAway → ALIGN_OFFSET + ALIGN_PARTNER_AWAY (Warning: the subject sits behind the
//                          OTHER station's stub, turning the subject cannot fix it) + ALIGN_DISTANCE +
//                          ALIGN_RISE.
//    WHERE (Round F6, item B1; findings/ui-small.md §2 and its Verification, correction 3). The game
//    does not lay tooltip groups out against each other: TooltipUISystem sends the mouse group (rows
//    from cursor + (0, 16) px down and right, TooltipUISystem.cs:14, :35-41, :56-61) and then every
//    world group (:62-65); each renders as its own 1 × 1 px box at its own screen point, later groups
//    on top. Our world group at the stub tip covered the object tool's own rows ("Place", "Rotate",
//    the cost) whenever the tip projected below the cursor (screenshot 20.46.09.59). So:
//      * subject is the tool's PREVIEW (m_Align.SubjectIsTemp: the object tool in Create / Move mode,
//        the preview sits under the cursor) → the rows go to the MOUSE list, one column with the
//        vanilla rows — the vanilla NetCourseTooltipSystem pattern (Game.UI.Tooltip/
//        NetCourseTooltipSystem.cs:67-75 keeps the widgets as children of its world group, :127-133
//        adds the same widgets to the mouse list instead). Our system follows TempCostTooltipSystem
//        (REGISTRATION; SystemOrder.cs:911) and InputHintsTooltipSystem is the last UITooltip system
//        (:933), so the column reads: cost, our rows, then Place / Rotate hints;
//      * the DEFAULT tool is active (a selected station, or one just moved: subjects (ii) / (iii))
//        → a WORLD group at the subject's stub tip, as GuideLineTooltipSystem does
//        (Game.UI.Tooltip/GuideLineTooltipSystem.cs:29-67: TooltipGroup with Center alignment and
//        Category.Network, position from TooltipSystemBase.WorldToTooltipPos, SetChildrenChanged when
//        the position moves; TooltipSystemBase.cs:20-30 AddGroup, :44-50 WorldToTooltipPos); nothing
//        while the tip is off screen;
//      * any OTHER tool, the subject a JUST-MOVED station (subject (ii), m_Align.SubjectIsMoved: e.g. a
//        station nudged with Move It's tool) → the rows go to the MOUSE list, as for a preview (Round E1
//        addendum A2, 2026-10-04: Round F6 B1 showed NOTHING here, so a Move It nudge showed the guide
//        lines but no numbers — field-test-6-fixes.md §4.3: "send subject (ii) rows to the mouse list
//        when the tool is not the default one"). In the mouse column our rows stack with the tool's own
//        rows and cover none of them;
//      * any OTHER tool, the subject only SELECTED (subject (iii)) → NOTHING, no-partner row included
//        (F6 B1 kept; E1 fixer pass 2, e1-spec §8.10 G1: S0 had applied A2 to every non-default tool, so a
//        station left selected added its rows — or "no station to align with" — under the hub's run
//        brush, the Net Tool, the bulldozer and the tree tool; ToolSystem.activeTool's setter does not
//        clear `selected`, ToolSystem.cs:84-99, and SelectedInfoUISystem.OnToolChanged only refreshes
//        the panel). Never the world group with such a tool: the case the F6 checker found — a station
//        selected in the world, then the hub's lift tile starts the Net Tool — put the world group on the
//        station's stub tip, exactly where the line starts, over the net tool's rows.
//      The rule is the pure StationAlign.PlaceOf (header READOUT PLACE; tests/LiftBatch).
//    NUMBER FORMAT (Round F6, item B6: this note used to say "angle with one decimal", which holds only
//    below 100°). The installed UI formats both units with Xc (1.6.2f1 index.js@508526): ONE decimal
//    below 100 with a trailing ".0" dropped, WHOLE numbers from 100 up, and any non-zero |v| < 0.1
//    shown as 0.1. "angle" calls Xc with one decimal (@507953), so a yaw of −179.6° prints "−180 °";
//    metric "length" uses Xc in metres below 1,000 m and kilometres with one decimal from 1,000 m
//    (@504557); unit names from @501249. Values are therefore rounded to 0.1 here and |v| < 0.05 is
//    sent as 0.
//    No partner → ALIGN_NO_PARTNER on the MOUSE (StringTooltip, Warning) — only for a TOP station
//    subject, or when a free station of the other kind exists but is behind / too far (fixer round,
//    finding l1-lifts-4: in the guided base-first order every base placement and every lone selected
//    base showed the warning; spec §5.5 literally says "when nothing qualifies"); same WHERE rule.
// 2. BULLDOZER WARNING while BulldozeToolSystem is the active tool: count Temp SkiChairliftHV edges
//    whose Temp flags carry Delete (TempFlags.Delete = 2, Game.Tools/TempFlags.cs:9) and sum their
//    Curve lengths — the bulldozer preview creates Delete definitions for the hovered target
//    (BulldozeToolSystem.cs:428-488; ApplyNetSystem.cs:438-441, per spec §5.7) [likely]:
//      1 section  → BULLDOZE_SECTION (Warning) + BULLDOZE_LENGTH;
//      ≥ 2, ONE TOWER's sections (IsOneTower: every deleted original edge meets one node and that node
//                   has no other lift section) → BULLDOZE_TOWER (Warning) + BULLDOZE_COUNT +
//                   BULLDOZE_LENGTH (a tower click deletes every connected section on the layer,
//                   BulldozeToolSystem.cs:592-641 per spec §5.1);
//      ≥ 2 otherwise (a bulldozer DRAG across several cables) → BULLDOZE_SECTIONS (Warning, neutral) +
//                   count + length (fixer round, finding l1-lifts-3: spec §5.7's "≥ 2 → tower" called a
//                   drag "a tower");
//      any Temp SkiLiftMarker edge with Delete → + BULLDOZE_STUB (Info).
//    Vanilla's tower semantics are not changed.
//
// MOUSE TOOLTIP MECHANICS as SkiGladeTooltipSystem (whose header carries the citations): widgets
// built once in OnCreate; StringTooltip.value boxes on compare, so string values are assigned once;
// NumberTooltip values compare through IEquatable (no box) and are assigned every frame;
// TooltipUISystem clears the mouse group and the groups, then runs UITooltip only while the mouse is
// not over UI (TooltipUISystem.cs:48-55), so everything is re-added every frame. Mod-unique paths:
// skiLiftAlign (group), skiLiftAlignStraight / Offset / Angle / FacingAway / PartnerAway / Distance /
// Rise (group children, or mouse rows while placing), skiLiftAlignNoPartner, skiLiftBulldozeSection /
// Tower / Sections / Count / Length / Stub. A widget is in the mouse list OR shown through the group in
// one frame, never both (AddMouseTooltip refuses a duplicate path, TooltipSystemBase.cs:32-42).
//
// REGISTRATION (I-4, Mod.cs): updateSystem.UpdateAfter<SkiLiftTooltipSystem,
// Game.UI.Tooltip.TempCostTooltipSystem>(SystemUpdatePhase.UITooltip) — the SkiGladeTooltipSystem
// anchor (TempCostTooltipSystem is UpdateAt UITooltip, SystemOrder.cs:911, 1.6.2 :912), so our lines
// follow the vanilla cost line of the bulldozer / placement preview.
//
// LOCALE KEYS (root LocaleEN.cs, I-4; texts are spec §2.4's): SkiResort.LiftTooltip.ALIGN_OFFSET,
// ALIGN_ANGLE, ALIGN_DISTANCE, ALIGN_RISE, ALIGN_STRAIGHT, ALIGN_NO_PARTNER, ALIGN_FACING_AWAY and
// ALIGN_PARTNER_AWAY (Round F6, B2), BULLDOZE_SECTION, BULLDOZE_TOWER, BULLDOZE_SECTIONS (fixer
// round), BULLDOZE_STUB, BULLDOZE_COUNT, BULLDOZE_LENGTH.
//
// HOUSE RULES: "created" line; one ARM line per load; Game mode only; m_Armed / m_Disabled /
// m_ErrorLogged per load; one-shot "!!!" catch that silences these tooltips until the next load.
// No per-frame managed allocation in our code (no LocalizedString.Id(key, args) per frame; the
// group's child list is rebuilt only when the readout class changes; the mouse rows are added by an
// indexed loop over that list — vanilla's AddMouseTooltip duplicate check is its own cost).

using System;
using System.Collections.Generic;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Game.UI.Localization;
using Game.UI.Tooltip;
using Game.UI.Widgets;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiLiftTooltipSystem : TooltipSystemBase
    {
        public const string KeyAlignOffset = "SkiResort.LiftTooltip.ALIGN_OFFSET";
        public const string KeyAlignAngle = "SkiResort.LiftTooltip.ALIGN_ANGLE";
        public const string KeyAlignDistance = "SkiResort.LiftTooltip.ALIGN_DISTANCE";
        public const string KeyAlignRise = "SkiResort.LiftTooltip.ALIGN_RISE";
        public const string KeyAlignStraight = "SkiResort.LiftTooltip.ALIGN_STRAIGHT";
        public const string KeyAlignNoPartner = "SkiResort.LiftTooltip.ALIGN_NO_PARTNER";
        public const string KeyAlignFacingAway = "SkiResort.LiftTooltip.ALIGN_FACING_AWAY";     // F6 B2
        public const string KeyAlignPartnerAway = "SkiResort.LiftTooltip.ALIGN_PARTNER_AWAY";   // F6 B2
        public const string KeyBulldozeSection = "SkiResort.LiftTooltip.BULLDOZE_SECTION";
        public const string KeyBulldozeTower = "SkiResort.LiftTooltip.BULLDOZE_TOWER";
        public const string KeyBulldozeSections = "SkiResort.LiftTooltip.BULLDOZE_SECTIONS";   // fixer round, l1-lifts-3
        public const string KeyBulldozeStub = "SkiResort.LiftTooltip.BULLDOZE_STUB";
        public const string KeyBulldozeCount = "SkiResort.LiftTooltip.BULLDOZE_COUNT";
        public const string KeyBulldozeLength = "SkiResort.LiftTooltip.BULLDOZE_LENGTH";

        private ToolSystem m_ToolSystem;
        private BulldozeToolSystem m_Bulldoze;
        private DefaultToolSystem m_DefaultTool;   // F6 B1: the world group only with this tool
        private SkiStationAlignSystem m_Align;
        private LiftCensusSystem m_Census;
        private EntityQuery m_TempEdgeQuery;

        // Alignment (world group, or mouse rows while placing — header 1 WHERE).
        private TooltipGroup m_AlignGroup;
        private StringTooltip m_Straight, m_FacingAway, m_PartnerAway;
        private FloatTooltip m_Offset, m_Angle, m_Distance, m_Rise;
        private int m_GroupLayout = -1;   // (int)StationAlign.Readout of the current child list; -1 = none
        private StringTooltip m_NoPartner;

        // Bulldozer (mouse).
        private StringTooltip m_SectionWarn, m_TowerWarn, m_SectionsWarn, m_Stub;
        private readonly List<Entity> m_DeletedOriginals = new List<Entity>(8);   // reused; never shrunk
        private IntTooltip m_Count;
        private FloatTooltip m_Length;

        private bool m_Armed, m_Disabled, m_ErrorLogged;

        /// <summary>Frames on which a lift tooltip was added (dev / field aid).</summary>
        public long AlignFramesShown, BulldozeFramesShown;

        protected override void OnCreate()
        {
            base.OnCreate();   // caches TooltipUISystem (TooltipSystemBase.cs:14-18)
            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_Bulldoze = World.GetOrCreateSystemManaged<BulldozeToolSystem>();
            m_DefaultTool = World.GetOrCreateSystemManaged<DefaultToolSystem>();
            m_Align = World.GetOrCreateSystemManaged<SkiStationAlignSystem>();
            m_Census = World.GetOrCreateSystemManaged<LiftCensusSystem>();

            m_TempEdgeQuery = GetEntityQuery(
                ComponentType.ReadOnly<Game.Net.Edge>(),
                ComponentType.ReadOnly<Temp>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadOnly<Curve>(),
                ComponentType.Exclude<Deleted>());

            m_Straight = new StringTooltip
            {
                path = "skiLiftAlignStraight",
                value = LocalizedString.Id(KeyAlignStraight),
                color = TooltipColor.Success,
            };
            m_FacingAway = new StringTooltip
            {
                path = "skiLiftAlignFacingAway",
                value = LocalizedString.Id(KeyAlignFacingAway),
                color = TooltipColor.Warning,
            };
            m_PartnerAway = new StringTooltip
            {
                path = "skiLiftAlignPartnerAway",
                value = LocalizedString.Id(KeyAlignPartnerAway),
                color = TooltipColor.Warning,
            };
            m_Offset = new FloatTooltip
            {
                path = "skiLiftAlignOffset",
                label = LocalizedString.Id(KeyAlignOffset),
                unit = "length",
                signed = true,
            };
            m_Angle = new FloatTooltip
            {
                path = "skiLiftAlignAngle",
                label = LocalizedString.Id(KeyAlignAngle),
                unit = "angle",
                signed = true,
            };
            m_Distance = new FloatTooltip
            {
                path = "skiLiftAlignDistance",
                label = LocalizedString.Id(KeyAlignDistance),
                unit = "length",
            };
            m_Rise = new FloatTooltip
            {
                path = "skiLiftAlignRise",
                label = LocalizedString.Id(KeyAlignRise),
                unit = "length",
                signed = true,
            };
            m_AlignGroup = new TooltipGroup
            {
                path = "skiLiftAlign",
                horizontalAlignment = TooltipGroup.Alignment.Center,
                verticalAlignment = TooltipGroup.Alignment.Center,
                category = TooltipGroup.Category.Network,
            };
            m_NoPartner = new StringTooltip
            {
                path = "skiLiftAlignNoPartner",
                value = LocalizedString.Id(KeyAlignNoPartner),
                color = TooltipColor.Warning,
            };

            m_SectionWarn = new StringTooltip
            {
                path = "skiLiftBulldozeSection",
                value = LocalizedString.Id(KeyBulldozeSection),
                color = TooltipColor.Warning,
            };
            m_TowerWarn = new StringTooltip
            {
                path = "skiLiftBulldozeTower",
                value = LocalizedString.Id(KeyBulldozeTower),
                color = TooltipColor.Warning,
            };
            m_SectionsWarn = new StringTooltip
            {
                path = "skiLiftBulldozeSections",
                value = LocalizedString.Id(KeyBulldozeSections),
                color = TooltipColor.Warning,
            };
            m_Stub = new StringTooltip
            {
                path = "skiLiftBulldozeStub",
                value = LocalizedString.Id(KeyBulldozeStub),
                color = TooltipColor.Info,
            };
            m_Count = new IntTooltip
            {
                path = "skiLiftBulldozeCount",
                label = LocalizedString.Id(KeyBulldozeCount),
                unit = "integer",
            };
            m_Length = new FloatTooltip
            {
                path = "skiLiftBulldozeLength",
                label = LocalizedString.Id(KeyBulldozeLength),
                unit = "length",
            };

            Mod.Log.Info($"{nameof(SkiLiftTooltipSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Armed = mode == GameMode.Game;
            m_Disabled = false;
            m_ErrorLogged = false;
            m_GroupLayout = -1;
            AlignFramesShown = BulldozeFramesShown = 0;
            if (!m_Armed)
                return;
            Mod.Log.Info("LiftTooltip: ARMED — with Lift building aids on: alignment readout on the mouse while " +
                         "placing (the tool's preview), at the station's stub tip with the default tool (a selected or " +
                         "just-moved station), on the mouse with any other tool for a station moved within " +
                         $"{SkiStationAlignSystem.MovedWindowS} s (e.g. a Move It nudge), none for a station only left selected " +
                         "(offset ± m, angle ± °, distance, rise; 'Straight — 180°' within " +
                         $"{StationAlign.AlignedOffsetM} m / {StationAlign.AlignedYawDeg}°; 'facing away' instead of the " +
                         $"angle beyond {StationAlign.FacingAwayDeg}°, 'the other station faces away' when behind its stub; " +
                         "'no free station' on the mouse for a " +
                         "top station, or when a free station of the other kind exists out of reach), and while " +
                         "the bulldozer is active the sections a click removes (1 section / a tower's N sections / N dragged " +
                         "sections + length; + the station's connection stub); UITooltip after TempCostTooltipSystem");
        }

        protected override void OnUpdate()
        {
            try
            {
                if (!m_Armed || m_Disabled)
                    return;
                if (Mod.Settings != null && !Mod.Settings.LiftBuildAids)
                    return;

                if (m_ToolSystem.activeTool == m_Bulldoze)
                    BulldozeWarning();

                if (m_Align.Active)
                    AlignReadout();
            }
            catch (Exception e)
            {
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiLiftTooltipSystem)} EXCEPTION — lift tooltips disabled until the next " +
                                  $"game load:\n{e}");
                }
            }
        }

        // ── 1. alignment readout ─────────────────────────────────────────────────────────────────
        private void AlignReadout()
        {
            // Header 1 WHERE (F6 B1, E1 addendum A2 as narrowed by fixer pass 2): the tool's preview →
            // mouse rows; the default tool → the world group; any other tool → mouse rows for a JUST-MOVED
            // station (Move It), nothing for a station that is only still selected — no-partner row
            // included, as in F6 (StationAlign.PlaceOf, header READOUT PLACE).
            StationAlign.ReadoutPlace place = StationAlign.PlaceOf(m_Align.SubjectIsTemp, m_Align.SubjectIsMoved,
                m_ToolSystem.activeTool == m_DefaultTool);
            if (place == StationAlign.ReadoutPlace.None)
                return;
            bool onMouse = place == StationAlign.ReadoutPlace.Mouse;

            if (!m_Align.HasPartner)
            {
                // Fixer round (finding l1-lifts-4): placing or selecting the FIRST station of a lift (a
                // base, in the guided base → top → line order) has nothing to align with — warn only
                // for a top station, or when a free station of the other kind exists but is behind it or
                // farther than the radius.
                if (!m_Align.SubjectIsBase || m_Align.OtherKindExists)
                    AddMouseTooltip(m_NoPartner);
                return;
            }
            StationAlign.Result r = m_Align.Last;
            StationAlign.Readout cls = StationAlign.Classify(r);   // F6 B2, header 1
            int layout = (int)cls;
            if (layout != m_GroupLayout)
            {
                m_GroupLayout = layout;
                m_AlignGroup.children.Clear();
                if (cls == StationAlign.Readout.Straight)
                {
                    m_AlignGroup.children.Add(m_Straight);
                }
                else
                {
                    m_AlignGroup.children.Add(m_Offset);
                    m_AlignGroup.children.Add(cls == StationAlign.Readout.FacingAway ? m_FacingAway
                        : cls == StationAlign.Readout.PartnerFacingAway ? (IWidget)m_PartnerAway
                        : m_Angle);
                }
                m_AlignGroup.children.Add(m_Distance);
                m_AlignGroup.children.Add(m_Rise);
                m_AlignGroup.SetChildrenChanged();
            }
            if (cls != StationAlign.Readout.Straight)
            {
                m_Offset.value = Tenths(r.LateralM);
                m_Angle.value = Tenths(r.YawDeg);
            }
            m_Distance.value = math.round(r.DistanceM);
            m_Rise.value = math.round(r.RiseM);

            if (onMouse)
            {
                // The preview sits under the cursor: stack with the vanilla rows in the mouse column
                // (NetCourseTooltipSystem.cs:127-133). Indexed loop: no enumerator.
                IList<IWidget> rows = m_AlignGroup.children;
                for (int i = 0; i < rows.Count; i++)
                    AddMouseTooltip(rows[i]);
                AlignFramesShown++;
                return;
            }

            float2 pos = WorldToTooltipPos(m_Align.SubjectTip, out bool onScreen);
            if (!onScreen)
                return;
            if (!m_AlignGroup.position.Equals(pos))
            {
                m_AlignGroup.position = pos;
                m_AlignGroup.SetChildrenChanged();   // as GuideLineTooltipSystem.cs:48-52
            }
            AddGroup(m_AlignGroup);
            AlignFramesShown++;
        }

        /// <summary>Rounded to 0.1, and exactly 0 below 0.05 (the UI shows any non-zero |v| &lt; 0.1 as 0.1).</summary>
        private static float Tenths(float v)
        {
            return math.abs(v) < 0.05f ? 0f : math.round(v * 10f) / 10f;
        }

        // ── 2. bulldozer warning ─────────────────────────────────────────────────────────────────
        private void BulldozeWarning()
        {
            if (m_TempEdgeQuery.IsEmptyIgnoreFilter)
                return;
            Entity lift = m_Census.ChairliftPrefab, marker = m_Census.MarkerPrefab;
            if (lift == Entity.Null)
                return;
            int sections = 0;
            float length = 0f;
            bool stub = false;
            m_DeletedOriginals.Clear();
            var chunks = m_TempEdgeQuery.ToArchetypeChunkArray(Allocator.Temp);
            try
            {
                var tempHandle = GetComponentTypeHandle<Temp>(isReadOnly: true);
                var prefabHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
                var curveHandle = GetComponentTypeHandle<Curve>(isReadOnly: true);
                for (int c = 0; c < chunks.Length; c++)
                {
                    var temps = chunks[c].GetNativeArray(ref tempHandle);
                    var prefabs = chunks[c].GetNativeArray(ref prefabHandle);
                    var curves = chunks[c].GetNativeArray(ref curveHandle);
                    for (int i = 0; i < temps.Length; i++)
                    {
                        if ((temps[i].m_Flags & TempFlags.Delete) == 0)
                            continue;
                        Entity prefab = prefabs[i].m_Prefab;
                        if (prefab == lift)
                        {
                            sections++;
                            length += curves[i].m_Length;
                            m_DeletedOriginals.Add(temps[i].m_Original);   // GenerateEdgesSystem.cs:1371-1377
                        }
                        else if (marker != Entity.Null && prefab == marker)
                        {
                            stub = true;
                        }
                    }
                }
            }
            finally
            {
                chunks.Dispose();
            }
            if (sections == 0 && !stub)
                return;

            if (sections == 1)
            {
                AddMouseTooltip(m_SectionWarn);
            }
            else if (sections >= 2)
            {
                // Header item 2 (fixer round, finding l1-lifts-3): the tower text only when the deleted
                // sections ARE one tower's sections; a drag over several cables gets the neutral text.
                AddMouseTooltip(IsOneTower(lift, sections) ? m_TowerWarn : m_SectionsWarn);
                m_Count.value = sections;
                AddMouseTooltip(m_Count);
            }
            if (sections > 0)
            {
                m_Length.value = math.round(length);
                AddMouseTooltip(m_Length);
            }
            if (stub)
                AddMouseTooltip(m_Stub);
            BulldozeFramesShown++;
        }

        /// <summary>Fixer round (finding l1-lifts-3): the deleted lift sections (their ORIGINAL edges,
        /// Temp.m_Original of the preview's Delete copies, GenerateEdgesSystem.cs:1371-1377, 1.6.2
        /// :1404-1410) all meet at one node AND that node has no other lift section (its ConnectedEdge
        /// buffer, lift prefab only) — i.e. the click removes exactly one tower's sections. A bulldozer
        /// drag across several cables (BulldozeToolSystem.cs:1548-1551, :1705-1707 add a control point
        /// per entity; :431-463 delete the edges of the dragged path) fails it. A 2-section drag through
        /// one tower passes it — its outcome is the same as clicking that tower (the node is orphaned and
        /// deleted with them, GenerateNodesSystem.cs:1250-1255), so the text stays true. Main thread;
        /// EntityManager reads sync their own writers; at most every frame while the bulldozer previews
        /// two or more lift sections.</summary>
        private bool IsOneTower(Entity liftPrefab, int sections)
        {
            if (m_DeletedOriginals.Count != sections || sections < 2)
                return false;
            if (!EntityManager.TryGetComponent(m_DeletedOriginals[0], out Game.Net.Edge first))
                return false;
            for (int pass = 0; pass < 2; pass++)
            {
                Entity node = pass == 0 ? first.m_Start : first.m_End;
                if (node == Entity.Null)
                    continue;
                bool shared = true;
                for (int k = 1; k < m_DeletedOriginals.Count && shared; k++)
                {
                    shared = EntityManager.TryGetComponent(m_DeletedOriginals[k], out Game.Net.Edge e) &&
                             (e.m_Start == node || e.m_End == node);
                }
                if (!shared || !EntityManager.HasBuffer<ConnectedEdge>(node))
                    continue;
                DynamicBuffer<ConnectedEdge> connected = EntityManager.GetBuffer<ConnectedEdge>(node, isReadOnly: true);
                int liftEdges = 0;
                for (int c = 0; c < connected.Length; c++)
                {
                    if (EntityManager.TryGetComponent(connected[c].m_Edge, out PrefabRef pr) && pr.m_Prefab == liftPrefab)
                        liftEdges++;
                }
                if (liftEdges == sections)
                    return true;
            }
            return false;
        }
    }
}
