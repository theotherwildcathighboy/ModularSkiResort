# LiftLineHotkeySystem: how mod key actions behave in the game (inactive tools, disabled-at-start actions, key conflicts)

`LiftLineHotkeySystem` owns the lift-line tool's two keys: ToggleLiftLineTool (default Ctrl+Shift+L) and BuildLiftLine (default Enter).

## Action plumbing
- An inactive tool never ticks (ToolSystem.cs:312-327), so the toggle key cannot be read from the tool itself; the hotkey system runs
  in UIUpdate (SystemOrder.cs:67), every frame, paused or not.
- Mod actions exist after `Mod.OnLoad`'s RegisterKeyBindings (ModSetting.cs:141-231) and start disabled (ProxyAction.cs:322-349); a
  system must set shouldBeEnabled to make one live.
- A key lost to a vanilla action is disabled by the conflict pass (InputConflictResolution.cs:138-198).
- Enter is live only while the lift tool or the grading tool is open, so it is never taken from the game otherwise. This system is the one
  writer of shouldBeEnabled for the Build key (two writers would fight every frame); `CatTrackGradeHotkeySystem` reads the press for its
  tool. The build request lands on the tool's next frame.

## Logs
One ARMED / INERT line per load with the bindings; one line when an armed action is not live 120 frames after the load (probable key
conflict); the tool logs every press it acts on.

The Build key is also live while the Quick runs tool is open (`buildLive`); `QuickRunsHotkeySystem` reads the press for that tool
(scan, build, plant) and owns its own toggle (ToggleQuickRunsTool, default Ctrl+Shift+Q, inert while the Quick runs option is off).
