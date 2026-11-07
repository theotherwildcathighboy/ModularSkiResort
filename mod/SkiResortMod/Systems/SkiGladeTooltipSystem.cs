// The mouse tooltip of the Glade tool: the glade tool is never silent. While it is the active tool, it shows these every frame:
// the target, and the tree density level and name. Then, by hover kind / plan state, it shows a hint, a warning, or "Trees now" /
// "Target" with the plan counts.
// Order (Mod.cs): UpdateAfter<SkiGladeTooltipSystem, Game.UI.Tooltip.TempCostTooltipSystem>(UITooltip), so our lines follow the
// vanilla cost line our Temps produce (SystemOrder.cs:911, 1.6.2 :912).
// Zero allocation per frame: widgets are built once. StringTooltip.value compares by boxing (StringTooltip.cs:18), so string
// values are assigned only when they change.
// Engine notes: EngineNotes/SkiGladeTooltipSystem.md

using System;
using Colossal.Serialization.Entities;
using Game;
using Game.UI.Localization;
using Game.UI.Tooltip;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiGladeTooltipSystem : TooltipSystemBase
    {
        public const string KeyHint = "SkiResort.GladeTooltip.HINT";
        public const string KeyNoRuns = "SkiResort.GladeTooltip.NO_RUNS";
        public const string KeyUnbound = "SkiResort.GladeTooltip.UNBOUND";
        public const string KeyBlocked = "SkiResort.GladeTooltip.BLOCKED";
        public const string KeyComplete = "SkiResort.GladeTooltip.COMPLETE";
        /// <summary>Complete below the target, spots blocked.</summary>
        public const string KeyCompleteBlocked = "SkiResort.GladeTooltip.COMPLETE_BLOCKED";
        /// <summary>Complete above the target, nothing fellable.</summary>
        public const string KeyCompleteFixed = "SkiResort.GladeTooltip.COMPLETE_FIXED";
        public const string KeyClamped = "SkiResort.GladeTooltip.CLAMPED";
        public const string KeySaturated = "SkiResort.GladeTooltip.SATURATED";
        public const string KeyMoney = "SkiResort.GladeTooltip.MONEY";
        public const string KeyTargetPrefix = "SkiResort.GladeTooltip.TARGET";
        public const string KeyLevel = "SkiResort.GladeTooltip.LEVEL";
        public const string KeyLevelNamePrefix = "SkiResort.GladeTooltip.LEVEL_NAME";
        public const string KeyTreesNow = "SkiResort.GladeTooltip.TREES_NOW";
        public const string KeyTreesTarget = "SkiResort.GladeTooltip.TREES_TARGET";
        public const string KeyPlant = "SkiResort.GladeTooltip.PLANT";
        public const string KeyFell = "SkiResort.GladeTooltip.FELL";
        public const string KeyRemove = "SkiResort.GladeTooltip.REMOVE";
        public const string KeyBlockedSpots = "SkiResort.GladeTooltip.BLOCKED_SPOTS";
        public const string KeyCatTrack = "SkiResort.GladeTooltip.CAT_TRACK";

        /// <summary>Built once. Index 0 = run interior, 1 = tree line.</summary>
        private static readonly LocalizedString[] TargetText =
        {
            LocalizedString.Id(KeyTargetPrefix + "[Interior]"),
            LocalizedString.Id(KeyTargetPrefix + "[TreeLine]"),
        };

        /// <summary>Built once. Index = level − 1 (LEVEL_NAME[1] … LEVEL_NAME[8]).</summary>
        private static readonly LocalizedString[] LevelNameText = BuildLevelNames();

        // Status line (one widget, value + color swapped on change). Index constants:
        private const int StHint = 0, StUnbound = 1, StBlocked = 2, StComplete = 3, StClamped = 4,
                          StSaturated = 5, StMoney = 6, StCompleteBlocked = 7, StCompleteFixed = 8, StCatTrack = 9;

        private static readonly LocalizedString[] StatusText =
        {
            LocalizedString.Id(KeyHint),
            LocalizedString.Id(KeyUnbound),
            LocalizedString.Id(KeyBlocked),
            LocalizedString.Id(KeyComplete),
            LocalizedString.Id(KeyClamped),
            LocalizedString.Id(KeySaturated),
            LocalizedString.Id(KeyMoney),
            LocalizedString.Id(KeyCompleteBlocked),
            LocalizedString.Id(KeyCompleteFixed),
            LocalizedString.Id(KeyCatTrack),
        };

        private static readonly TooltipColor[] StatusColor =
        {
            TooltipColor.Info,      // HINT
            TooltipColor.Warning,   // UNBOUND
            TooltipColor.Warning,   // BLOCKED
            TooltipColor.Info,      // COMPLETE
            TooltipColor.Warning,   // CLAMPED
            TooltipColor.Info,      // SATURATED
            TooltipColor.Warning,   // MONEY
            TooltipColor.Info,      // COMPLETE_BLOCKED
            TooltipColor.Info,      // COMPLETE_FIXED
            TooltipColor.Info,      // CAT_TRACK
        };

        private SkiGladeToolSystem m_Tool;

        private StringTooltip m_Target;
        private IntTooltip m_Level;
        private StringTooltip m_LevelName;
        private IntTooltip m_TreesNow;
        private IntTooltip m_TreesTarget;
        private StringTooltip m_Status;
        private StringTooltip m_NoRuns;
        private IntTooltip m_Plant;
        private IntTooltip m_Fell;
        private IntTooltip m_Remove;
        private IntTooltip m_BlockedSpots;
        private int m_ShownTarget = -1;   // target index currently written into m_Target.value
        private int m_ShownLevel = -1;    // level currently written into m_LevelName.value
        private int m_ShownStatus = -1;   // status index currently written into m_Status

        // Per-load state — reset at OnGameLoadingComplete.
        private bool m_Armed;
        private bool m_Disabled;
        private bool m_ErrorLogged;

        /// <summary>Frames on which the lines were added since the last load (dev / field aid).</summary>
        public long FramesShown;

        private static LocalizedString[] BuildLevelNames()
        {
            var names = new LocalizedString[SkiGladeCompose.Levels];
            for (int i = 0; i < names.Length; i++)
                names[i] = LocalizedString.Id(KeyLevelNamePrefix + "[" + (i + 1) + "]");
            return names;
        }

        protected override void OnCreate()
        {
            base.OnCreate(); // caches TooltipUISystem (TooltipSystemBase.cs:14-18)
            m_Tool = World.GetOrCreateSystemManaged<SkiGladeToolSystem>();

            m_Target = new StringTooltip
            {
                path = "skiGladeTarget",
                color = TooltipColor.Info,
            };
            m_Level = new IntTooltip
            {
                path = "skiGladeLevel",
                label = LocalizedString.Id(KeyLevel),
                unit = "integer",
            };
            m_LevelName = new StringTooltip
            {
                path = "skiGladeLevelName",
                color = TooltipColor.Info,
            };
            m_TreesNow = new IntTooltip
            {
                path = "skiGladeTreesNow",
                label = LocalizedString.Id(KeyTreesNow),
                unit = "integer",
            };
            m_TreesTarget = new IntTooltip
            {
                path = "skiGladeTreesTarget",
                label = LocalizedString.Id(KeyTreesTarget),
                unit = "integer",
            };
            m_Status = new StringTooltip
            {
                path = "skiGladeHint",
                color = TooltipColor.Info,
            };
            m_NoRuns = new StringTooltip
            {
                path = "skiGladeNoRuns",
                value = LocalizedString.Id(KeyNoRuns),
                color = TooltipColor.Warning,
            };
            m_Plant = new IntTooltip
            {
                path = "skiGladePlant",
                label = LocalizedString.Id(KeyPlant),
                unit = "integer",
            };
            m_Fell = new IntTooltip
            {
                path = "skiGladeFell",
                label = LocalizedString.Id(KeyFell),
                unit = "integer",
            };
            m_Remove = new IntTooltip
            {
                path = "skiGladeRemove",
                label = LocalizedString.Id(KeyRemove),
                unit = "integer",
            };
            m_BlockedSpots = new IntTooltip
            {
                path = "skiGladeBlocked",
                label = LocalizedString.Id(KeyBlockedSpots),
                unit = "integer",
            };

            Mod.Log.Info($"{nameof(SkiGladeTooltipSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            m_Armed = false;
            m_Disabled = false;
            m_ErrorLogged = false;
            FramesShown = 0;

            if (mode != GameMode.Game)
                return;

            m_Armed = true;
            Mod.Log.Info("GladeTooltip: ARMED — while the glade tool is active: target (run interior / tree " +
                         "line), tree density L (1-8) and its name, then by hover: hint (+ 'no run linked' when " +
                         "the city has no bound run), 'run not linked' (Warning) for unbound/legacy runs, and on a " +
                         "bound run 'Trees now' and 'Target' (last compose), then 'cannot be planned' (Warning) / " +
                         "complete: 'nothing to change' only at the target, else 'remaining spots blocked' (+ " +
                         "the shortfall) below it or 'remaining trees cannot be felled' above it / counts to " +
                         "plant, fell and remove (non-zero only), blocked spots known (planting previews only), " +
                         "'apply again' (Warning) / 'no room' / 'not enough money?' (Warning); counts are " +
                         "the emitted plan until validated, then the Temp census; paths skiGladeTarget/" +
                         "skiGladeLevel/skiGladeLevelName/skiGladeTreesNow/skiGladeTreesTarget/skiGladeHint/" +
                         "skiGladeNoRuns/skiGladePlant/skiGladeFell/skiGladeRemove/skiGladeBlocked; UITooltip " +
                         "after TempCostTooltipSystem");
        }

        protected override void OnUpdate()
        {
            try
            {
                if (m_Disabled || !m_Armed)
                    return;
                // Cheapest gates first (reference compare inside IsActive, ToolSystem.cs:84-99).
                if (!m_Tool.IsActive || !m_Tool.IsArmed || m_Tool.IsDisabled)
                    return;

                // 1. Target. Assign the struct only on change (object.Equals boxes, engine notes).
                int target = m_Tool.TreeLineTarget ? 1 : 0;
                if (target != m_ShownTarget)
                {
                    m_Target.value = TargetText[target];
                    m_ShownTarget = target;
                }
                AddMouseTooltip(m_Target);

                // 2./3. Tree density L and its name.
                int level = m_Tool.Level;
                if (level < 1 || level > LevelNameText.Length)
                    level = SkiGladeCompose.DefaultLevel;   // defensive: never garbage
                m_Level.value = level;
                AddMouseTooltip(m_Level);
                if (level != m_ShownLevel)
                {
                    m_LevelName.value = LevelNameText[level - 1];
                    m_ShownLevel = level;
                }
                AddMouseTooltip(m_LevelName);

                // 4. Hover kind / plan state.
                switch (m_Tool.HoverKind)
                {
                    case GladeHoverKind.Bound:
                        ShowBound();
                        break;
                    case GladeHoverKind.Unbound:
                    case GladeHoverKind.Legacy:
                        ShowStatus(StUnbound);
                        break;
                    case GladeHoverKind.Foreign when m_Tool.HoverOnCatTrack:
                        ShowStatus(StCatTrack);
                        break;
                    default:
                        // Terrain / Foreign / Nothing, and the transient None / Switching / Held.
                        ShowStatus(StHint);
                        if (m_Tool.CityBoundRuns == 0)
                            AddMouseTooltip(m_NoRuns);
                        break;
                }

                FramesShown++;
            }
            catch (Exception e)
            {
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiGladeTooltipSystem)} EXCEPTION — glade tooltip " +
                                  $"disabled until the next game load:\n{e}");
                }
            }
        }

        private void ShowBound()
        {
            GladePlanState state = m_Tool.PlanState;
            if (state == GladePlanState.Blocked)
            {
                ShowStatus(StBlocked);
                return;
            }

            // "Trees now N", "Target T" for a bound run with a compose result.
            if (m_Tool.PlanHasCounts)
            {
                m_TreesNow.value = m_Tool.PlanNow;
                AddMouseTooltip(m_TreesNow);
                m_TreesTarget.value = m_Tool.PlanTarget;
                AddMouseTooltip(m_TreesTarget);
            }

            if (state == GladePlanState.Complete)
            {
                // "already at this tree density" only when it is true. Plain int compares, no allocation.
                bool counts = m_Tool.PlanHasCounts;
                if (counts && m_Tool.PlanNow < m_Tool.PlanTarget && m_Tool.PlanShortfall > 0)
                {
                    m_BlockedSpots.value = m_Tool.PlanShortfall;
                    AddMouseTooltip(m_BlockedSpots);
                    ShowStatus(StCompleteBlocked);
                }
                else if (counts && m_Tool.PlanNow > m_Tool.PlanTarget)
                {
                    ShowStatus(StCompleteFixed);
                }
                else
                {
                    ShowStatus(StComplete);
                }
                return;
            }

            bool ready = m_Tool.PreviewReady;
            int plant = ready ? m_Tool.PreviewPlant - m_Tool.PreviewCancelled : m_Tool.PlannedPlant;
            int fell = ready ? m_Tool.PreviewThin : m_Tool.PlannedFell;
            int remove = ready ? m_Tool.PreviewRemove : m_Tool.PlannedRemove;
            if (plant > 0)
            {
                m_Plant.value = plant;
                AddMouseTooltip(m_Plant);
            }
            if (fell > 0)
            {
                m_Fell.value = fell;
                AddMouseTooltip(m_Fell);
            }
            if (remove > 0)
            {
                m_Remove.value = remove;
                AddMouseTooltip(m_Remove);
            }
            // Blocked spots matter only to a preview that plants or falls short. The skip set is level-independent, so a fell /
            // remove preview would show spots it never used.
            if (m_Tool.PreviewBlockedKnown > 0 && (plant > 0 || (m_Tool.PlanHasCounts && m_Tool.PlanShortfall > 0)))
            {
                m_BlockedSpots.value = m_Tool.PreviewBlockedKnown;
                AddMouseTooltip(m_BlockedSpots);
            }

            switch (state)
            {
                case GladePlanState.Clamped:
                    ShowStatus(StClamped);
                    break;
                case GladePlanState.Saturated:
                    ShowStatus(StSaturated);
                    break;
                case GladePlanState.MoneyShort:
                    ShowStatus(StMoney);
                    break;
            }
        }

        /// <summary>One status widget. The code writes value and color only when the status changes.</summary>
        private void ShowStatus(int status)
        {
            if (status != m_ShownStatus)
            {
                m_Status.value = StatusText[status];
                m_Status.color = StatusColor[status];
                m_ShownStatus = status;
            }
            AddMouseTooltip(m_Status);
        }
    }
}
