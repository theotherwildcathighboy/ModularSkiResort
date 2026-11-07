# SkiRunTooltipSystem: the mouse-tooltip lines shown while drawing a ski run, and the vanilla tooltip machinery they rely on

The mouse tooltip that shows while the vanilla area tool draws or edits one of our ski runs. Engine citations are from the 1.6.0f1
decompile baseline, unchanged in 1.6.2f1.

## The lines

Up to eight lines, on screen in the order 1-6, 8, 7:

1. Band name ("Blue square", ...): `StringTooltip`, Info (Warning for Flat). It shows the rated band
   (`SkiRunDrawFeedbackSystem.TryGetDifficulty`).
2. "Hardest unavoidable pitch: 32 %": `IntTooltip`, unit "percentage". The value is `RatingGrade`, the crux of the easiest line. With a
   broken Mask (`Readout.Connected` false, P90 fallback), the line reads "Typical steep pitch (run outline broken, rating
   approximate): 32 %" in Warning color, with its own path.
3. "Length: 864 m": `IntTooltip`, unit "length". The value is `Readout.Length`, horizontal meters.
4. "Vertical drop: 140 m": `IntTooltip`, unit "length" (meters in, the UI converts).
5. "Avg 18 ° (32 %) · steepest 31 ° (60 %)", `StringTooltip`, path `skiRunSlope`: the live grade, so the
   player sees why the run will rate as it does before placing it. The line uses `LocalizedString.Id(SLOPE_AVG_MAX)` with four
   `LocalizedNumber<int>` args (units "angle" / "percentage"). The code rebuilds it only when a rounded number changes. The numbers come
   from `SkiRunRater.SlopeReadout`. A suffix names the deciding factor (`SkiRunRater.NoteLine`, keys `SLOPE_AVG_MAX_NARROW` / `_WIDE` /
   `_SHARE` / `_GENTLE`). It is unverified in game how a `LocalizedNumber` arg with these units renders (vanilla passes units this way,
   ModManager.cs:341, GraphicsSettings.cs:654).
6. "Steepest patch (avoidable): 45 %": `IntTooltip`. It shows only when `SkiSlopeClassifier.SteepestPatchShown` is true (Connected, a real
   margin above the crux, a different rounded percentage, a higher band than shown). It explains red cells on a run rated easier. It
   never shows in polyline mode.
7. "No Ski Area Welcome Sign within 5 km": `StringTooltip`, Warning, new runs only.
8. Run fee (one-time): `IntTooltip`, money unit, Money icon, new runs only. The value is `SkiRunFees.Fee(kind, plan area)`, the function
   that the bind charges. It has Error color when the city has less money (TempCostTooltipSystem.cs:79-82). Label key
   `SkiResort.Tooltip.RUN_FEE`, path "skiRunFee", inputs `SkiRunDrawFeedbackSystem.TryGetFeeInputs`. It shows even when the rating
   failed. The vanilla cost line does not show for our areas (Temp areas carry no `m_Cost`, and `AreaToolTooltipSystem` shows nothing
   for a plain Surface). So there is no double price. Like the whole feedback system, the row follows the "Show run grade while
   drawing" setting.

## How a mod tooltip works

`TooltipSystemBase` (TooltipSystemBase.cs:9) caches `TooltipUISystem` in `OnCreate` (:14-18). `AddMouseTooltip` appends to
`mouseGroup.children` and drops a duplicate non-empty path with a `LogError` (:32-42). So our paths are mod-unique (skiRunGrade,
skiRunPitch, skiRunPitchApprox, skiRunLength, skiRunDrop, skiRunSlope, skiRunSteepest, skiRunNoGate, skiRunFee). At most one of the two
pitch lines shows per frame. `TooltipUISystem` clears `mouseGroup.children` and then runs the whole UITooltip phase. It does this only
while the "tooltip.groups" binding is active, the mouse is not over UI and tooltips are not hidden (TooltipUISystem.cs:48-55). So this
`OnUpdate` re-adds its lines every frame they should show and needs no over-UI check. The phase runs from UIUpdate (`UIUpdateSystem` at
SystemOrder.cs:67). It likely updates while paused, but this is not field-tested.

## Order

`UpdateAfter<SkiRunTooltipSystem, AreaToolTooltipSystem>(UITooltip)` in Mod.cs. A plain `UpdateAt` sorts by add index
(UpdateSystem.cs:39-47) and would put our lines after `InputHintsTooltipSystem` (SystemOrder.cs:933). `AreaToolTooltipSystem`
(SystemOrder.cs:916) shows nothing for a plain Surface (AreaToolTooltipSystem.cs:101, 176-183). So our lines follow the (normally
absent) cost line and precede notifications and input hints.

## Widgets

The code builds the widgets once in `OnCreate` as fields (vanilla: TempCostTooltipSystem.cs:44-56, AreaToolTooltipSystem.cs:78-95) and
never subclasses them. `Widget.propertiesTypeName` is `GetType().FullName` (Widget.cs:74), and the JS renderer knows only the
`Game.UI.Tooltip.*` names. `IntTooltip` renders because `NumberTooltip<T>` overrides it (NumberTooltip.cs:80). `TooltipColor` has only
Info/Success/Warning/Error (TooltipColor.cs:3-9), so bands cannot be colored blue/black. The band name carries the color. `IntTooltip`
has a color too (NumberTooltip.cs:6, LabelIconTooltip.cs:7, IconTooltip.cs:31), which the approximate-pitch line uses. A label is a
`LocalizedString` compared with `object.Equals` (LabelIconTooltip.cs:21, boxing), so each label lives on its own widget, set once in
`OnCreate`. Locale keys come from the root LocaleEN.cs. `LocalizedString.Id(key)` is a plain struct ctor (:39-42). A bare string
converts implicitly to a key, not raw text (:124-127). `Id(key, args...)` must not run per frame (it builds a Dictionary through LINQ,
:44-48).

## Zero allocation per frame

`StringTooltip.value` / `LabelIconTooltip.label` compare with `object.Equals` on the `LocalizedString` struct (StringTooltip.cs:18),
which boxes both sides. So the code assigns the band value only when the band changes. `NumberTooltip<int>.value` compares through
`IEquatable<int>` (NumberTooltip.cs:24), and `IconTooltip.color` is an enum compare (IconTooltip.cs:39). So the code assigns those every
frame. Their setters flag a change only when the value differs. This cost is not ours to avoid: the base `AddMouseTooltip` runs
`children.Any(lambda)` (TooltipSystemBase.cs:34), the cost every vanilla tooltip system pays.

## Data

The data comes from `SkiRunDrawFeedbackSystem.TryGetReadout`, computed in the Rendering phase of the previous frame (MainLoop runs
UIUpdate before Rendering, SystemOrder.cs:67-68). This is a one-frame lag by design. The gate is not the prefab of the tool. In Modify,
the area tool edits any Surface regardless of the selected prefab (AreaToolSystem.cs:320-322). The Temp carries the prefab of the
original run (GenerateAreasSystem.cs:172-175). The feedback system filters on that.

## Scope

Game mode only. House contract:
- "created" line.
- ARMED line per load.
- `m_Armed` / `m_Disabled` / `m_ErrorLogged` reset in `OnGameLoadingComplete`.
- One-shot "!!!" catch that silences the tooltip until the next game load.
