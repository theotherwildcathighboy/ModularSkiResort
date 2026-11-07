# SkiPlacementInfoviewSystem: why and how the Electricity infoview is closed while placing lifts

Engine citations are `Game.X/File.cs:line` in tools/decompile/1.6.0f1. "1.6.2 :N" marks a shift in the installed 1.6.2f1 tree.
Unmarked = identical in 1.6.2.

The system closes the Electricity infoview that the vanilla tools open. This happens when the player starts drawing one of our lift LINES or placing
one of our STATIONS. The system does this once per tool activation, and it leaves the view open when the player had it open before. The
targets are:
- Our lift LINES: every found `SkiCatalog.Lifts` row with `CloseElectricityView`.
- Our STATIONS: every found `SkiCatalog.Stations` row (base and top of every lift type, the tram terminals included). There is no mid
  station yet. A new station row is a target by being in that list.

Targets come from `SkiCatalog` and their `PrefabBase` from `SkiPrefabCatalogSystem` (`LinePrefabBase`, `StationPrefabBase`: PrefabID
fast path, then its one name scan). The system writes `!!! Infoview: ... not found` only for an expected row
(`LiftTypeDef.LineExpected`, `StationDef.Expected`) that is missing. The ARMED line lists pending rows (not imported yet)
("pending: ...").

Rule (2026-11-02): no vanilla electricity view or icon on a lift except the base station's own low-voltage "not connected" icon
(LiftLineIconRule, EngineNotes/LiftLineIconSystem.md). The view drew our lifts as power lines. That icon signals the base station's
missing feed, so no station keeps the view. Until then the base station kept it (its low-voltage feed was run there).

## Why code, not prefab data

No prefab field names the placement infoview. `InfoviewInitializeSystem` scores every infoview per placeable prefab and writes the
winners into its `PlaceableInfoviewItem` buffer (InfoviewInitializeSystem.cs:277-428, :601-656). The lift scores Electricity
through `PowerLineData` (:410-415, the lift is a power line). A station scores it through its Transformer (:290, the lift's power door).
Both causes are load-bearing. A station whose prefab scores no view is a harmless target: the close needs the tool's own view open.

## Mechanism

- A tool's infoview is element [0] of its prefab's `PlaceableInfoviewItem` buffer. `UpdateInfoview` writes it into
  `ToolBaseSystem.infoview` (ToolBaseSystem.cs:176, :477-504). The net tool calls `UpdateInfoview` from its OnUpdate every frame
  (NetToolSystem.cs:6045, 1.6.2 :6183).
- `ToolSystem.ToolUpdate` applies it only when the active tool's infoview differs from the last one it applied
  (`m_LastToolInfoview`, ToolSystem.cs:321-342). `SetInfoview` (:377-399) replaces the view. ToolSystem runs in MainLoop before
  UIUpdate (SystemOrder.cs:58, :67), so at our UIUpdate the tool's view of this frame is already applied.
- The public setter `ToolSystem.infoview` (:118-131) is the vanilla infoview menu's own call (`InfoviewsUISystem.SetActiveInfoview`,
  InfoviewsUISystem.cs:431-439). Writing null closes the view. The next ToolUpdate does not reopen it, because the tool's infoview
  still equals `m_LastToolInfoview` (:333) and the re-apply arm needs `activeInfoview` == the tool's view (:343). A tool switch
  (bulldozer and back) makes the tool's view differ once and reopens it. We then close it again (a new activation, the latch is
  cleared).
- `activePrefab` = `m_ActiveTool.GetPrefab()` (ToolSystem.cs:116, `NetToolSystem.GetPrefab` :5548-5555, 1.6.2 :5676). We call
  `activeTool.GetPrefab()` ourselves so a null activeTool cannot throw. `activeInfoview` (ToolSystem.cs:133-143) is null for an
  invalid view.

## Rule

On an activePrefab change, clear the latch. `playerHadIt` = the view at our previous update equaled the new tool's infoview and that
view was not opened by a tool. Then the system acts when all of these conditions hold:
- the prefab is a target
- `activeInfoview != null`
- `activeInfoview == activeTool.infoview`
- the latch is clear

If `playerHadIt`, latch and count Kept ("Infoview [keep]"). Otherwise `ToolSystem.infoview = null`, latch, Closed++
("Infoview [close]"). Re-opening the view by hand during the same activation is kept.

"The view equaled the tool's at the previous update" is also true when the previous tool opened it (a vanilla transformer tool opens
Electricity, :290). So we track who opened the current view. A view change seen in the same
update as a tool/prefab change, equal to the tool's own infoview, is tool-opened. Any other view change is the player's.

Cost: a one-frame Electricity overlay flash per activation (colors recompute in the next ToolUpdate, ToolSystem.cs:369-373).
Unverified in game.

## Restore (lift tool to another tool with the same view)

Our close goes through the public setter, which changes only `m_CurrentInfoview` (`SetInfoview`, ToolSystem.cs:377-399).
ToolSystem's private `m_LastToolInfoview` keeps Electricity. It is written only inside ToolUpdate's
`infoviewPrefab != m_LastToolInfoview` arm (:333-336), and the re-apply arm needs `activeInfoview` == the tool's view, null after
our close. So a direct switch from a lift tool to another tool with the same Electricity view would leave that tool without its view.
Such a tool is a vanilla power line or transformer. `ToolSystem.ActivatePrefabTool` switches tools with no default tool between
(:263-277). A switch to another of our targets (line -> station tile) keeps it closed, which is the rule.

`m_ClosedView` = the view we closed. The system clears it in either of these cases:
- The active tool's view differs from it (the cache moved on: first arm, :333-336).
- Any view is open again.

While `m_ClosedView` is set, we reopen the view when all of these hold:
- the active prefab is not a target
- the tool's own view is that closed view
- no view is open

We reopen it with the same public setter, latch it as tool-opened, log one capped `Infoview [restore]` line and clear `m_ClosedView`.
The setter passes no infomode list (`SetInfoview(value, null)`). So every optional infomode is on and the tool's supplemental ones are
not (:388). This is the same as when the vanilla infoview menu opens a view (InfoviewsUISystem.cs:431-439). The next ToolUpdate's re-apply arm
(:343-367) reconciles them.

Not headless-testable (four reference compares on game objects). It needs a field test: lift line -> vanilla low-voltage line directly
-> Electricity is open. Lift line -> Base station tile -> no view.

## Phase (Mod.cs)

`UpdateBefore<SkiPlacementInfoviewSystem, Game.UI.InGame.InfoviewsUISystem>(UIUpdate)` only. Adding UpdateAt as well would register
the system twice and run it twice per frame (UpdateSystem.cs:158-176 -> Register :271-292 appends one SystemData per call, no
de-duplication). `InfoviewsUISystem` is UIUpdate (SystemOrder.cs:939, 1.6.2 :940), so the vanilla infoview binding sees our close in
the same frame (its OnInfoviewChanged handler, InfoviewsUISystem.cs:138, :174-177). UIUpdate runs every frame, paused included
(SystemOrder.cs:67).

## Logs

The system writes these log lines:
- a "created" line
- one "Infoview: ARMED" line per load, naming the targets and every constant
- a one-shot "!!!" catch that disables the system until the next load
- a "!!!" line when the target prefab is missing (the view then stays open while drawing lifts)

It writes at most one line per tool activation or restore. These lines are capped per load (`LogLinesMax`), with one "cap reached"
line. So a long
bulldoze/redraw session cannot flood. Game mode only. Per frame: three property reads and a few reference compares.
