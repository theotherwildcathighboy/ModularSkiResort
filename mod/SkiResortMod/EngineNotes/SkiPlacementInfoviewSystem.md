# SkiPlacementInfoviewSystem: why and how the Electricity infoview is closed while drawing lifts

Engine citations are `Game.X/File.cs:line` in tools/decompile/1.6.0f1; "1.6.2 :N" marks a shift in the installed 1.6.2f1 tree,
unmarked = identical in 1.6.2.

The system closes the Electricity infoview that the vanilla net tool opens when the player starts drawing one of our lift LINES
(every found `SkiCatalog.Lifts` row with `CloseElectricityView`), once per tool activation, and leaves it open when the player had it
open before. Targets come from `SkiCatalog` and their `PrefabBase` from `SkiPrefabCatalogSystem` (PrefabID fast path, then its one
name scan). `!!! Infoview: ... not found` only for an expected line (`LiftTypeDef.LineExpected`) that is missing; pending lines (not
imported yet) are listed in the ARMED line ("pending: ..."). The base station keeps its view: its low-voltage feed is run there
(LocaleEN.cs, the base station's description).

## Why code, not prefab data

No prefab field names the placement infoview. `InfoviewInitializeSystem` scores every infoview per placeable prefab and writes the
winners into its `PlaceableInfoviewItem` buffer (InfoviewInitializeSystem.cs:277-428, :601-656); the lift scores Electricity
through `PowerLineData` (:410-415, the lift is a power line), the base station through its Transformer (:290, the lift's power
door). Both causes are load-bearing.

## Mechanism

- A tool's infoview is element [0] of its prefab's `PlaceableInfoviewItem` buffer, written into `ToolBaseSystem.infoview` by
  `UpdateInfoview` (ToolBaseSystem.cs:176, :477-504), which the net tool calls from its OnUpdate every frame (NetToolSystem.cs:6045,
  1.6.2 :6183).
- `ToolSystem.ToolUpdate` applies it only when the active tool's infoview differs from the last one it applied
  (`m_LastToolInfoview`, ToolSystem.cs:321-342); `SetInfoview` (:377-399) replaces the view. ToolSystem runs in MainLoop before
  UIUpdate (SystemOrder.cs:58, :67), so at our UIUpdate the tool's view of this frame is already applied.
- The public setter `ToolSystem.infoview` (:118-131) is the vanilla infoview menu's own call (`InfoviewsUISystem.SetActiveInfoview`,
  InfoviewsUISystem.cs:431-439). Writing null closes the view; the next ToolUpdate does not reopen it, because the tool's infoview
  still equals `m_LastToolInfoview` (:333) and the re-apply arm needs `activeInfoview` == the tool's view (:343). A tool switch
  (bulldozer and back) makes the tool's view differ once and reopens it; we then close it again (a new activation, the latch is
  cleared).
- `activePrefab` = `m_ActiveTool.GetPrefab()` (ToolSystem.cs:116; `NetToolSystem.GetPrefab` :5548-5555, 1.6.2 :5676). We call
  `activeTool.GetPrefab()` ourselves so a null activeTool cannot throw. `activeInfoview` (ToolSystem.cs:133-143) is null for an
  invalid view.

## Rule

On an activePrefab change, clear the latch; `playerHadIt` = the view at our previous update equaled the new tool's infoview and that
view was not opened by a tool. Then, when the prefab is a target, `activeInfoview != null`, `activeInfoview == activeTool.infoview`
and the latch is clear: `playerHadIt` latches and counts Kept ("Infoview [keep]"); otherwise `ToolSystem.infoview = null`, latch,
Closed++ ("Infoview [close]"). Re-opening the view by hand during the same activation is kept.

"The view equaled the tool's at the previous update" is also true when the previous tool opened it (the base station tool opens
Electricity, :290; the order base -> line is common), so we track who opened the current view: a view change seen in the same
update as a tool/prefab change, equal to the tool's own infoview, is tool-opened; any other view change is the player's.

Cost: a one-frame Electricity overlay flash per activation (colors recompute in the next ToolUpdate, ToolSystem.cs:369-373);
unverified in game.

## Restore (lift tool to another tool with the same view)

Our close goes through the public setter, which changes only `m_CurrentInfoview` (`SetInfoview`, ToolSystem.cs:377-399);
ToolSystem's private `m_LastToolInfoview` keeps Electricity (it is written only inside ToolUpdate's
`infoviewPrefab != m_LastToolInfoview` arm, :333-336, and the re-apply arm needs `activeInfoview` == the tool's view, null after
our close). So a direct switch from the lift tool to another tool with the same Electricity view (the hub's Base station tile:
`ToolSystem.ActivatePrefabTool` switches net tool -> object tool with no default tool between, :263-277; or the vanilla low-voltage
line) would leave that tool without its view.

`m_ClosedView` = the view we closed; it is cleared as soon as the active tool's view differs from it (the cache moved on: first
arm, :333-336) or any view is open again. While it is set, when the active prefab is not a target, the tool's own view is that
closed view and no view is open, we reopen it with the same public setter, latch it as tool-opened, log one capped
`Infoview [restore]` line and clear `m_ClosedView`. The setter passes no infomode list (`SetInfoview(value, null)`), so every
optional infomode is on and the tool's supplemental ones are not (:388), as when the vanilla infoview menu opens a view
(InfoviewsUISystem.cs:431-439); the next ToolUpdate's re-apply arm (:343-367) reconciles them.

Not headless-testable (four reference compares on game objects); needs a field test: lift line -> Base station tile directly ->
Electricity is open.

## Phase (Mod.cs)

`UpdateBefore<SkiPlacementInfoviewSystem, Game.UI.InGame.InfoviewsUISystem>(UIUpdate)` only; adding UpdateAt as well would register
the system twice and run it twice per frame (UpdateSystem.cs:158-176 -> Register :271-292 appends one SystemData per call, no
de-duplication). `InfoviewsUISystem` is UIUpdate (SystemOrder.cs:939, 1.6.2 :940), so the vanilla infoview binding sees our close in
the same frame (its OnInfoviewChanged handler, InfoviewsUISystem.cs:138, :174-177). UIUpdate runs every frame, paused included
(SystemOrder.cs:67).

## Logs

"created" line; one "Infoview: ARMED" line per load naming the targets and every constant; a one-shot "!!!" catch that disables the
system until the next load; a "!!!" line when the target prefab is missing (the view then stays open while drawing lifts). At most
one line per tool activation or restore, capped per load (`LogLinesMax`) with one "cap reached" line, so a long bulldoze/redraw
session cannot flood. Game mode only; per frame: three property reads and a few reference compares.
