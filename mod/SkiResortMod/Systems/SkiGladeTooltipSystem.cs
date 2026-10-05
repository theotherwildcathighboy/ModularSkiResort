// SkiGladeTooltipSystem — the mouse tooltip of the Glade tool (docs/research/run-binding-glade-spec.md
// §4.9, Round G2, as amended by §8.4, Round G3; DECISIONS 121 DEFECT 3: "the glade tool is never
// silent"). Created 2026-10-01 (Round G2, implementer I-3); Round G3 2026-10-01 (implementer G3-B):
// the mode / layout-variant lines became the target / tree-density lines, plus "Trees now" and
// "Target". While the glade tool is the active tool it shows, every frame:
//
//   1. Target                     StringTooltip  SkiResort.GladeTooltip.TARGET[Interior|TreeLine]
//   2. Tree density (1-8)  L      IntTooltip     SkiResort.GladeTooltip.LEVEL, unit "integer"
//   3. level name                 StringTooltip  SkiResort.GladeTooltip.LEVEL_NAME[1..8]
//   4. by hover kind / plan state (SkiGladeToolSystem.HoverKind / PlanState, spec §4.7):
//        Terrain / Foreign / Nothing (and the one-frame None / Switching / Held states)
//            → HINT "Hover a ski run to plan trees" (+ NO_RUNS when CityBoundRuns == 0)
//        Unbound / Legacy        → UNBOUND (Warning)
//        Bound, Blocked          → BLOCKED (Warning)
//        Bound, otherwise        → "Trees now N" + "Target T" (IntTooltips TREES_NOW /
//                                   TREES_TARGET, unit "integer", from the tool's last compose,
//                                   SkiGladeToolSystem.PlanNow / PlanTarget, shown while
//                                   PlanHasCounts), then for a complete plan ONE of:
//                                     N < T with Shortfall > 0 → BLOCKED_SPOTS (= Shortfall, the
//                                       trees the stream could not place) + COMPLETE_BLOCKED;
//                                     N > T → COMPLETE_FIXED (the trees left inside may not be
//                                       felled: other runs', owned, burning);
//                                     else → COMPLETE ("already at this tree density");
//                                   else the PLANT / FELL / REMOVE counts (only the non-zero
//                                   ones), BLOCKED_SPOTS (= the known blocked spots of the plan)
//                                   only when the preview PLANTS or falls short — a fell / remove
//                                   preview never shows it — and CLAMPED (Warning) / SATURATED /
//                                   MONEY (Warning) for those states (spec §8.4 as amended in the
//                                   G3 fixer round, finding tool-2: G3 returned on Complete before
//                                   the blocked-spot line, so an L8 run k trees short read
//                                   "Nothing to change: already at this tree density", and showed
//                                   the whole level-independent skip set on fell previews).
//   The vanilla cost line comes from TempCostTooltipSystem, which needs our Temps
//   (TempCostTooltipSystem.cs:57 RequireForUpdate on Temp), so it shows with any live preview.
//
// COUNTS. Until the preview is validated (the frame after emission) the tool's PlannedPlant /
// PlannedFell / PlannedRemove (what it emitted) are shown; from then on the census: plant =
// PreviewPlant − PreviewCancelled (what an Apply plants), fell = PreviewThin, remove =
// PreviewRemove (Round G3: both count only Delete Temps without Cancel — a cancelled delete keeps
// its tree, ApplyObjectsSystem.cs:337-340, 474-482). "Trees now" counts every tree inside the run
// that the census sees, including trees the game keeps hidden as Overridden (the §8.1 defect fix,
// SkiGladeToolSystem header), so it can exceed what the eye counts.
//
// HOW A MOD TOOLTIP WORKS (as SkiRunTooltipSystem, whose header has the full citations; none of
// the cited Game.UI.Tooltip files changed in 1.6.2f1). TooltipSystemBase caches TooltipUISystem
// (TooltipSystemBase.cs:14-18); AddMouseTooltip drops a duplicate non-empty path with a LogError
// (:32-42), so every path here is mod-unique: skiGladeTarget, skiGladeLevel, skiGladeLevelName,
// skiGladeTreesNow, skiGladeTreesTarget, skiGladeHint, skiGladeNoRuns, skiGladePlant,
// skiGladeFell, skiGladeRemove, skiGladeBlocked. TooltipUISystem clears the mouse group and THEN
// runs the UITooltip phase only while the mouse is not over UI and tooltips are not hidden
// (TooltipUISystem.cs:48-55), so this OnUpdate re-adds its lines every frame and needs no over-UI
// check.
//
// ORDER (registered by I-4 in Mod.cs): UpdateAfter<SkiGladeTooltipSystem,
// Game.UI.Tooltip.TempCostTooltipSystem>(SystemUpdatePhase.UITooltip) — TempCostTooltipSystem is
// UpdateAt UITooltip at Game.Common/SystemOrder.cs:911 (1.6.2 :912), so our lines follow the
// vanilla cost line our Temps produce.
//
// ZERO ALLOCATION PER FRAME. Widgets are built once in OnCreate (vanilla: TempCostTooltipSystem.cs
// :44-56). StringTooltip.value compares with object.Equals on the LocalizedString struct
// (StringTooltip.cs:18 — boxes), so the target, level-name and status values are assigned only
// when they change; a status line swaps value + colour on one widget, as vanilla
// CityPolicyTooltipSystem.cs:139-159 does. NumberTooltip<int>.value compares through
// IEquatable<int> (NumberTooltip.cs:24, no box) and IconTooltip.color is an enum compare
// (IconTooltip.cs:31-39), so those are assigned every frame. LocalizedString.Id(key) is a struct
// ctor; never Id(key, args) per frame (LINQ Dictionary). The base AddMouseTooltip runs
// children.Any(lambda) (TooltipSystemBase.cs:34) — the cost every vanilla tooltip system pays.
// Unit "integer" for counts: HomelessTooltipSystem.cs:31-36 (identical in 1.6.2).
//
// SCOPE: Game mode only. Defensive contract per house rules: "created" line, ARMED line per load,
// m_Armed/m_Disabled/m_ErrorLogged reset in OnGameLoadingComplete, one-shot "!!!" catch that
// silences the tooltip until the next game load (the tool's own log lines keep it non-silent).

using System;
using Colossal.Serialization.Entities;
using Game;
using Game.UI.Localization;
using Game.UI.Tooltip;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiGladeTooltipSystem : TooltipSystemBase
    {
        // Locale keys (root LocaleEN.cs; texts are spec §2's and §8.3's). Round G3 retired
        // SkiResort.GladeTooltip.MODE[*] and SkiResort.GladeTooltip.VARIANT.
        public const string KeyHint = "SkiResort.GladeTooltip.HINT";
        public const string KeyNoRuns = "SkiResort.GladeTooltip.NO_RUNS";
        public const string KeyUnbound = "SkiResort.GladeTooltip.UNBOUND";
        public const string KeyBlocked = "SkiResort.GladeTooltip.BLOCKED";
        public const string KeyComplete = "SkiResort.GladeTooltip.COMPLETE";
        /// <summary>G3 fixer round (finding tool-2): complete below the target, spots blocked.</summary>
        public const string KeyCompleteBlocked = "SkiResort.GladeTooltip.COMPLETE_BLOCKED";
        /// <summary>G3 fixer round (finding behaviour-2): complete above the target, nothing fellable.</summary>
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

        /// <summary>Built once; index 0 = run interior, 1 = tree line.</summary>
        private static readonly LocalizedString[] TargetText =
        {
            LocalizedString.Id(KeyTargetPrefix + "[Interior]"),
            LocalizedString.Id(KeyTargetPrefix + "[TreeLine]"),
        };

        /// <summary>Built once; index = level − 1 (LEVEL_NAME[1] … LEVEL_NAME[8]).</summary>
        private static readonly LocalizedString[] LevelNameText = BuildLevelNames();

        // Status line (one widget, value + colour swapped on change). Index constants:
        private const int StHint = 0, StUnbound = 1, StBlocked = 2, StComplete = 3, StClamped = 4,
                          StSaturated = 5, StMoney = 6, StCompleteBlocked = 7, StCompleteFixed = 8;

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

                // 1. Target. Assign the struct only on change (object.Equals boxes, header).
                int target = m_Tool.TreeLineTarget ? 1 : 0;
                if (target != m_ShownTarget)
                {
                    m_Target.value = TargetText[target];
                    m_ShownTarget = target;
                }
                AddMouseTooltip(m_Target);

                // 2./3. Tree density L and its name (spec §8.4 "Tree density L with the level name").
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

            // Spec §8.4: "Trees now N", "Target T" for a bound run with a compose result.
            if (m_Tool.PlanHasCounts)
            {
                m_TreesNow.value = m_Tool.PlanNow;
                AddMouseTooltip(m_TreesNow);
                m_TreesTarget.value = m_Tool.PlanTarget;
                AddMouseTooltip(m_TreesTarget);
            }

            if (state == GladePlanState.Complete)
            {
                // G3 fixer round (findings tool-2 / behaviour-2): "already at this tree density"
                // only when it is true. Plain int compares, no allocation.
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
            // Blocked spots matter only to a preview that plants or falls short (finding tool-2:
            // the skip set is level-independent, so a fell / remove preview showed spots it never
            // used).
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

        /// <summary>One status widget; value and colour written only when the status changes.</summary>
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
