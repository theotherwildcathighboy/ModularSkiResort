# SkiLiftTooltipSystem: where the lift station alignment readout and bulldozer warning tooltips go, and why

The lift building aids' tooltips: the station alignment readout and the bulldozer warning (Settings > Lifts > LiftBuildAids).

## 1. Alignment readout

State comes from `SkiStationAlignSystem`, which runs at Rendering. TooltipUISystem is a UIUpdate system, and UIUpdate precedes
Rendering in MainLoop (SystemOrder.cs:67-68). So this is the PREVIOUS frame's measurement (one frame of lag, accepted).

The rows, by `StationAlign.Classify` (see the READOUT CLASS notes in the StationAlign header):

- Numbers: ALIGN_OFFSET (FloatTooltip, unit "length", signed: + left / - right of the partner's axis seen from the partner,
  StationAlign SIGN CONVENTION), ALIGN_ANGLE (unit "angle", signed), ALIGN_DISTANCE (length), ALIGN_RISE (length, signed).
- Straight: ALIGN_STRAIGHT (StringTooltip, Success) + ALIGN_DISTANCE + ALIGN_RISE.
- FacingAway: ALIGN_OFFSET + ALIGN_FACING_AWAY (StringTooltip, Warning, in place of the angle: |yaw| > 90 degrees would read
  "-180 deg", the same 180 as the green "Straight - 180 deg") + ALIGN_DISTANCE + ALIGN_RISE.
- PartnerFacingAway: ALIGN_OFFSET + ALIGN_PARTNER_AWAY (Warning: the subject sits behind the OTHER station's stub, and turning the
  subject cannot fix it) + ALIGN_DISTANCE + ALIGN_RISE.

### Where the rows go

The game does not lay tooltip groups out against each other. TooltipUISystem sends the mouse group (rows from cursor + (0, 16) px down
and right, TooltipUISystem.cs:14, :35-41, :56-61). Then it sends every world group (:62-65). Each group renders as its own 1 x 1 px box
at its own screen point, with later groups on top. A world group at the stub tip would cover the object tool's own rows ("Place",
"Rotate", the cost) whenever the tip projects below the cursor. So:

- Subject is the tool's PREVIEW (`m_Align.SubjectIsTemp`: the object tool in Create / Move mode, and the preview sits under the
  cursor). The rows go to the MOUSE list, in one column with the vanilla rows. This is the NetCourseTooltipSystem pattern.
  NetCourseTooltipSystem.cs:67-75 keeps the widgets as children of its world group. :127-133 adds the same widgets to the mouse list
  instead. Our system follows TempCostTooltipSystem (see Registration), and InputHintsTooltipSystem is the last UITooltip system
  (SystemOrder.cs:933). So the column reads: cost, our rows, then Place / Rotate hints.
- The DEFAULT tool is active (a selected station, or one just moved): the rows go to a WORLD group at the subject's stub tip, as
  GuideLineTooltipSystem does. GuideLineTooltipSystem.cs:29-67 makes a TooltipGroup with Center alignment and Category.Network. It takes
  the position from TooltipSystemBase.WorldToTooltipPos. It calls SetChildrenChanged when the position moves. TooltipSystemBase.cs:20-30
  is AddGroup, and :44-50 is WorldToTooltipPos. The system shows nothing while the tip is off screen.
- Any OTHER tool, the subject a JUST-MOVED station (`m_Align.SubjectIsMoved`, for example nudged with Move It's tool). The rows go to the MOUSE list, as for a preview, so they stack with the tool's own rows and cover none of them.
- Any OTHER tool, the subject only SELECTED: show NOTHING, no-partner row included. ToolSystem.activeTool's setter does not clear
  `selected` (ToolSystem.cs:84-99), and SelectedInfoUISystem.OnToolChanged only refreshes the panel. So a station left selected would
  add rows under the hub's run brush, the Net Tool, the bulldozer and the tree tool. Never use the world group with such a tool.
  Example: a player selects a station in the world, then the hub's lift tile starts the Net Tool. The group would go on the station's
  stub tip, exactly where the line starts, over the net tool's rows.

The rule is the pure `StationAlign.PlaceOf` (READOUT PLACE in its header, tests/LiftBatch).

### Number format

The installed UI formats both units with Xc (1.6.2f1 index.js@508526). The format is:

- ONE decimal below 100, with a trailing ".0" dropped.
- WHOLE numbers from 100 up.
- Any non-zero |v| < 0.1 shown as 0.1.

"angle" calls Xc with one decimal (@507953), so a yaw of -179.6 degrees prints "-180 deg". Metric "length" uses Xc in meters below
1,000 m and in kilometers with one decimal from 1,000 m (@504557). Unit names come from @501249. Values are therefore rounded to 0.1
here, and |v| < 0.05 is sent as 0.

### No partner

ALIGN_NO_PARTNER goes on the MOUSE (StringTooltip, Warning). It shows in two cases:

- The subject is a TOP station.
- A free station of the other kind exists but is behind / too far.

In the guided base-first order, a base has nothing to align with yet. The same WHERE rule applies.

## 2. Bulldozer warning

While BulldozeToolSystem is the active tool, the system does two steps:

- It counts Temp lift-line edges (every FOUND line in `SkiPrefabCatalogSystem.LiftLinePrefabSet`, all lift types) whose Temp flags carry
  Delete (TempFlags.Delete = 2, Game.Tools/TempFlags.cs:9).
- It sums their Curve lengths.

The bulldozer preview creates Delete definitions for the hovered target (BulldozeToolSystem.cs:428-488 and ApplyNetSystem.cs:438-441)
[likely]. The rows are:

- 1 section: BULLDOZE_SECTION (Warning) + BULLDOZE_LENGTH.
- >= 2, ONE TOWER's sections: BULLDOZE_TOWER (Warning) + BULLDOZE_COUNT + BULLDOZE_LENGTH. IsOneTower means every deleted original edge meets one node and that node has no other lift section. A tower click deletes every connected section on the layer (BulldozeToolSystem.cs:592-641).
- >= 2 otherwise (a bulldozer DRAG across several cables): BULLDOZE_SECTIONS (Warning, neutral) + count + length.
- Any Temp SkiLiftMarker edge (`SkiPrefabCatalogSystem.MarkerEntity`) with Delete: + BULLDOZE_STUB (Info).

Vanilla's tower semantics are not changed.

## Mouse tooltip mechanics

These work as in SkiGladeTooltipSystem (see SkiGladeTooltipSystem.md for the citations). The paths are mod-unique, because AddMouseTooltip
refuses a duplicate path (TooltipSystemBase.cs:32-42). The paths are: skiLiftAlign (group), skiLiftAlignStraight / Offset / Angle / FacingAway /
PartnerAway / Distance / Rise (group children, or mouse rows while placing), skiLiftAlignNoPartner, skiLiftBulldozeSection / Tower /
Sections / Count / Length / Stub. A widget is in the mouse list OR shown through the group in one frame, never both.

## Registration

Mod.cs: `UpdateAfter<SkiLiftTooltipSystem, Game.UI.Tooltip.TempCostTooltipSystem>(UITooltip)`, the SkiGladeTooltipSystem anchor,
so our lines follow the vanilla cost line of the bulldozer / placement preview.

## House rules

- "created" line.
- One ARM line per load.
- Game mode only.
- One-shot "!!!" catch that silences these tooltips until the next load.
- No per-frame managed allocation in our code:
  - No LocalizedString.Id(key, args) per frame.
  - The system rebuilds the group's child list only when the readout class changes.
  - An indexed loop over that list adds the mouse rows.
