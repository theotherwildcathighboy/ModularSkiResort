# LiftLineHotkeySystem: how mod key actions behave in the game (inactive tools, disabled-at-start actions, key conflicts)

`LiftLineHotkeySystem` owns the lift-line tool's two keys: ToggleLiftLineTool (default Ctrl+Shift+L) and BuildLiftLine (default Enter).

## Action plumbing
- An inactive tool never ticks (ToolSystem.cs:312-327). The toggle key therefore cannot be read from the tool itself. The hotkey system
  runs in UIUpdate (SystemOrder.cs:67), every frame, paused or not.
- Mod actions exist after the RegisterKeyBindings call in `Mod.OnLoad` (ModSetting.cs:141-231). They start disabled (ProxyAction.cs:322-349).
  A system must set shouldBeEnabled to make one live.
- The conflict pass disables a key that a vanilla action took (InputConflictResolution.cs:138-198).
- Enter is live only while the lift tool or the grading tool is open, so the system never takes it from the game otherwise. This system is
  the one writer of shouldBeEnabled for the Build key, because two writers would fight every frame. `CatTrackGradeHotkeySystem` reads the
  press for its tool. The build request lands on the tool's next frame.

## Logs
- One ARMED / INERT line per load, with the bindings.
- One line when an armed action is not live 120 frames after the load (probable key conflict).
- The tool logs every press it acts on.

The Build key is also live while the Quick runs tool is open (`buildLive`). `QuickRunsHotkeySystem` reads the press for that tool
(scan, build, plant). It also owns its own toggle (ToggleQuickRunsTool, default Ctrl+Shift+Q, inert while the Quick runs option is off).
