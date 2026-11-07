# SkiGladeTooltipSystem: what the Glade tool's mouse tooltip shows and how it stays allocation-free

The mouse tooltip of the Glade tool. The glade tool is never silent. While it is the active tool, it shows the lines
below every frame.

## Lines shown

1. Target: StringTooltip `SkiResort.GladeTooltip.TARGET[Interior|TreeLine]`.
2. Tree density (1-8) L: IntTooltip `SkiResort.GladeTooltip.LEVEL`, unit "integer".
3. Level name: StringTooltip `SkiResort.GladeTooltip.LEVEL_NAME[1..8]`.
4. The lines depend on the hover kind and plan state (`SkiGladeToolSystem.HoverKind` / `PlanState`):
   - Terrain / Foreign / Nothing (and the one-frame None / Switching / Held states): HINT "Hover a ski run to plan trees" (+ NO_RUNS
     when CityBoundRuns == 0).
   - Unbound / Legacy: UNBOUND (Warning).
   - Bound, Blocked: BLOCKED (Warning).
   - Bound, otherwise: "Trees now N" + "Target T" (IntTooltips TREES_NOW / TREES_TARGET, unit "integer", from the tool's last
     compose, `SkiGladeToolSystem.PlanNow` / `PlanTarget`, shown while `PlanHasCounts`). Then, for a complete plan, ONE of:
     - N < T with Shortfall > 0: BLOCKED_SPOTS (= Shortfall, the trees the stream could not place) + COMPLETE_BLOCKED.
     - N > T: COMPLETE_FIXED (the trees left inside may not be felled: other runs', owned, burning).
     - Else: COMPLETE ("already at this tree density").

     For a plan that is not complete, the tooltip shows:
     - the PLANT / FELL / REMOVE counts (only the non-zero ones)
     - BLOCKED_SPOTS (= the known blocked spots of the plan), only when the preview PLANTS or falls short. The skip set is
       level-independent, so a fell / remove preview would show spots it never used
     - CLAMPED (Warning) / SATURATED / MONEY (Warning) for those states

     A complete plan must not return before the blocked-spot line: an L8 run k trees short would read "already at this tree density".

The vanilla cost line comes from TempCostTooltipSystem, which needs our Temps (TempCostTooltipSystem.cs:57 RequireForUpdate on
Temp), so it shows with any live preview.

## Counts

Until the preview is validated (the frame after emission), the tooltip shows the tool's PlannedPlant / PlannedFell / PlannedRemove
(what it emitted). From then on it shows the census: plant = PreviewPlant - PreviewCancelled (what an Apply plants), fell =
PreviewThin, remove = PreviewRemove. Fell and remove count only Delete Temps without Cancel: a canceled delete keeps its tree
(ApplyObjectsSystem.cs:337-340, 474-482). "Trees now" counts every tree inside the run that the census sees. This includes trees the game
keeps hidden as Overridden (see SkiGladeToolSystem.md, "Why Overridden trees are counted"). So it can exceed what the eye counts.

## How a mod tooltip works

This works as in SkiRunTooltipSystem, whose header has the full citations. None of the cited Game.UI.Tooltip files changed in 1.6.2f1.
TooltipSystemBase caches TooltipUISystem (TooltipSystemBase.cs:14-18). AddMouseTooltip drops a duplicate non-empty path with a
LogError (:32-42). So every path here is mod-unique. The paths are: skiGladeTarget, skiGladeLevel, skiGladeLevelName, skiGladeTreesNow,
skiGladeTreesTarget, skiGladeHint, skiGladeNoRuns, skiGladePlant, skiGladeFell, skiGladeRemove, skiGladeBlocked. TooltipUISystem
clears the mouse group and THEN runs the UITooltip phase only while the mouse is not over UI and tooltips are not hidden
(TooltipUISystem.cs:48-55). So this OnUpdate re-adds its lines every frame and needs no over-UI check.

## Order

Mod.cs: `UpdateAfter<SkiGladeTooltipSystem, Game.UI.Tooltip.TempCostTooltipSystem>(SystemUpdatePhase.UITooltip)`.
TempCostTooltipSystem is UpdateAt UITooltip at Game.Common/SystemOrder.cs:911 (1.6.2 :912), so our lines follow the vanilla cost
line our Temps produce.

## Zero allocation per frame

OnCreate builds the widgets once (vanilla: TempCostTooltipSystem.cs:44-56). `StringTooltip.value` compares with object.Equals on
the LocalizedString struct (StringTooltip.cs:18, boxes). So the system assigns the target, level-name and status values only when
they change. A status line swaps value + color on one widget, as vanilla CityPolicyTooltipSystem.cs:139-159 does.
`NumberTooltip<int>.value` compares through IEquatable<int> (NumberTooltip.cs:24, no box), and `IconTooltip.color` is an enum
compare (IconTooltip.cs:31-39). So the system assigns those every frame. `LocalizedString.Id(key)` is a struct ctor. Never use
Id(key, args) per frame (LINQ Dictionary). The base AddMouseTooltip runs children.Any(lambda) (TooltipSystemBase.cs:34), the cost
every vanilla tooltip system pays. Unit "integer" for counts: HomelessTooltipSystem.cs:31-36 (identical in 1.6.2).

## Scope

Game mode only. The system writes a "created" line and an ARMED line per load. It resets m_Armed/m_Disabled/m_ErrorLogged in
OnGameLoadingComplete. A one-shot "!!!" catch silences the tooltip until the next game load (the tool's own log lines keep it
non-silent).
