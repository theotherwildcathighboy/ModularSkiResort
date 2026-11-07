// The lift building aids' tooltips: the station alignment readout and the bulldozer warning (Settings > Lifts > LiftBuildAids).
// The alignment state comes from SkiStationAlignSystem (Rendering). TooltipUISystem runs earlier in MainLoop (SystemOrder.cs:67-68),
// so the readout is the PREVIOUS frame's measurement (one frame of lag, accepted).
// The pure StationAlign.PlaceOf decides where the rows go:
// - mouse list for a preview or just-moved subject,
// - world group at the stub tip for the default tool,
// - nothing for any other tool with a merely selected station.
// Registration (Mod.cs): UpdateAfter<SkiLiftTooltipSystem, Game.UI.Tooltip.TempCostTooltipSystem>(UITooltip), after the vanilla cost line.
// No per-frame managed allocation. A one-shot "!!!" catch silences these tooltips until the next load.
// Engine notes: EngineNotes/SkiLiftTooltipSystem.md

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
        public const string KeyAlignFacingAway = "SkiResort.LiftTooltip.ALIGN_FACING_AWAY";
        public const string KeyAlignPartnerAway = "SkiResort.LiftTooltip.ALIGN_PARTNER_AWAY";
        public const string KeyBulldozeSection = "SkiResort.LiftTooltip.BULLDOZE_SECTION";
        public const string KeyBulldozeTower = "SkiResort.LiftTooltip.BULLDOZE_TOWER";
        public const string KeyBulldozeSections = "SkiResort.LiftTooltip.BULLDOZE_SECTIONS";
        public const string KeyBulldozeStub = "SkiResort.LiftTooltip.BULLDOZE_STUB";
        public const string KeyBulldozeCount = "SkiResort.LiftTooltip.BULLDOZE_COUNT";
        public const string KeyBulldozeLength = "SkiResort.LiftTooltip.BULLDOZE_LENGTH";

        private ToolSystem m_ToolSystem;
        private BulldozeToolSystem m_Bulldoze;
        private DefaultToolSystem m_DefaultTool;  // the world group only with this tool
        private SkiStationAlignSystem m_Align;
        // The lift lines and the marker come from the catalog.
        private SkiPrefabCatalogSystem m_PrefabCatalog;
        private EntityQuery m_TempEdgeQuery;

        // Alignment (world group, or mouse rows while placing — engine notes 1 WHERE).
        private TooltipGroup m_AlignGroup;
        private StringTooltip m_Straight, m_FacingAway, m_PartnerAway;
        private FloatTooltip m_Offset, m_Angle, m_Distance, m_Rise;
        private int m_GroupLayout = -1;   // (int)StationAlign.Readout of the current child list. -1 = none
        private StringTooltip m_NoPartner;

        // Bulldozer (mouse).
        private StringTooltip m_SectionWarn, m_TowerWarn, m_SectionsWarn, m_Stub;
        private readonly List<Entity> m_DeletedOriginals = new List<Entity>(8);   // reused, never shrunk
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
            m_PrefabCatalog = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>();

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

        private void AlignReadout()
        {
            // Engine notes 1 WHERE (StationAlign.PlaceOf, engine notes READOUT PLACE):
            // - the tool's preview → mouse rows.
            // - the default tool → the world group.
            // - any other tool → mouse rows for a JUST-MOVED station (Move It), nothing for a station that is only still selected, no-partner row included.
            StationAlign.ReadoutPlace place = StationAlign.PlaceOf(m_Align.SubjectIsTemp, m_Align.SubjectIsMoved,
                m_ToolSystem.activeTool == m_DefaultTool);
            if (place == StationAlign.ReadoutPlace.None)
                return;
            bool onMouse = place == StationAlign.ReadoutPlace.Mouse;

            if (!m_Align.HasPartner)
            {
                // Placing or selecting the FIRST station of a lift (a base, in the guided base → top → line order) has nothing to align
                // with. The tooltip warns only in these cases:
                // - for a top station,
                // - when a free station of the other kind exists but is behind it or farther than the radius.
                if (!m_Align.SubjectIsBase || m_Align.OtherKindExists)
                    AddMouseTooltip(m_NoPartner);
                return;
            }
            StationAlign.Result r = m_Align.Last;
            StationAlign.Readout cls = StationAlign.Classify(r);
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

        private void BulldozeWarning()
        {
            if (m_TempEdgeQuery.IsEmptyIgnoreFilter)
                return;
            // The catalog's live line set (fetched once, before the chunk loop) and marker. No line FOUND → no warning (the catalog's
            // ARM line and the census's "!!!" name the miss).
            HashSet<Entity> lines = m_PrefabCatalog.LiftLinePrefabSet;
            Entity marker = m_PrefabCatalog.MarkerEntity;
            if (lines.Count == 0)
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
                        if (lines.Contains(prefab))
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
                // The tower text only when the deleted sections ARE one tower's sections. A drag over several cables gets the neutral
                // text.
                AddMouseTooltip(IsOneTower(lines, sections) ? m_TowerWarn : m_SectionsWarn);
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

        /// <summary>The deleted lift sections (their ORIGINAL edges, Temp.m_Original of the preview's Delete copies,
        /// GenerateEdgesSystem.cs:1371-1377, 1.6.2 :1404-1410) all meet at one node AND that node has no other lift section (its
        /// ConnectedEdge buffer, lift prefab only). That means the click removes exactly one tower's sections.
        /// A bulldozer drag across several cables fails it. The tool adds a control point per entity (BulldozeToolSystem.cs:1548-1551,
        /// :1705-1707) and deletes the edges of the dragged path (:431-463).
        /// A 2-section drag through one tower passes it. Its outcome is the same as clicking that tower, so the text stays true
        /// (the node is orphaned and deleted with them, GenerateNodesSystem.cs:1250-1255).
        /// It runs on the main thread. EntityManager reads sync their own writers. It runs at most every frame while the bulldozer previews
        /// two or more lift sections. A lift section is an edge of any catalog lift line (the live LiftLinePrefabSet the caller fetched).</summary>
        private bool IsOneTower(HashSet<Entity> liftPrefabs, int sections)
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
                    if (EntityManager.TryGetComponent(connected[c].m_Edge, out PrefabRef pr) && liftPrefabs.Contains(pr.m_Prefab))
                        liftEdges++;
                }
                if (liftEdges == sections)
                    return true;
            }
            return false;
        }
    }
}
