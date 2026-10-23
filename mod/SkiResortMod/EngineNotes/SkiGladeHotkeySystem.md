# SkiGladeHotkeySystem: why a separate polling system, how mod key actions are registered, and how conflicts drop them

Decompile baseline `Game.dll` 1.6.0f1.

## Why a separate system

The tool cannot poll its own toggle: `ToolBaseSystem.OnCreate` sets `Enabled = false` (`ToolBaseSystem.cs:310`) and
`ToolSystem.ToolUpdate` enables ONLY the active tool (`ToolSystem.cs:312-327`), so an inactive tool never ticks. This system runs at
`SystemUpdatePhase.UIUpdate`, driven by `UIUpdateSystem` in MainLoop (`SystemOrder.cs:67`, `UIUpdateSystem.cs:19`): every frame, paused
or not (GameSimulation would stop while paused).

## Key bindings and actions

The keys are `ModSetting` `ProxyBinding`s (`Settings/SkiResortSettings.cs`). They become input actions in our own map only when
`Mod.OnLoad` calls `RegisterKeyBindings` (`ModSetting.cs:141-231`). `GetAction(name)` is `FindAction(id, name)` and returns null before
that (`ModSetting.cs:289-292`; `InputManager.cs:570-573`), so resolution is retried at every load. Mod actions start DISABLED:
`shouldBeEnabled` reads a lazily created activator (`ProxyAction.cs:322-349`). A mod action is never built-in (`ModSetting.cs:181`),
so the setter does not throw. We enable each resolved action per game mode in `OnGameLoadingComplete`: live in a loaded city, off in
the main menu and the editor, which UIUpdate also ticks. `WasPressedThisFrame` is the Unity source action's (`ProxyAction.cs:494-497`)
and is false while the action is disabled.

What each key does:

- toggle (default Ctrl+Shift+G): `tool.IsActive ? tool.Deactivate() : tool.TryActivate("hotkey")`.
- `GladeNextMode` (Alt+G): `tool.ToggleTarget` (run interior / tree line); `GladeNextVariant` (Shift+G): `tool.StepLevel` (tree density
  1..8, wrap); persisted action names are unchanged. Forwarded ONLY while the tool is active (never polled inside the tool: one press
  must not toggle twice). The tool writes the one log line per press.
- dump (default Ctrl+Alt+G): polled BEFORE the toggle and the tool-active gate, in any tool: area tool active ->
  `SkiRunDrawFeedbackSystem.DumpToLog()`; glade tool active -> `SkiGladeToolSystem.DumpToLog()`; otherwise both. UIUpdate precedes
  Rendering in MainLoop (`SystemOrder.cs:67-68`), so the feedback dump reads the PREVIOUS frame's readout with the cursor still on the
  map (a dev-tab button press loses the Temp).

Resolution is per action: the three glade actions arm together (they only make sense together) and the dump action arms on its own.
The ARMED line names each.

## Conflicts

A mod action always loses a key conflict to an enabled vanilla system/UI action and is then disabled (`InputConflictResolution.cs:138-198`:
system -> UI and mod, UI -> mod; each pair via `InputManager.HasConflicts`, which calls `ProxyBinding.ConflictsWith(x, y, checkUsage:
false)`, `InputManager.cs:732-769`, and only for actions ENABLED at that moment). The `State.enabled` -> `Apply` ->
`ProxyAction.ApplyState` path (`InputConflictResolution.cs:14-24`, 37-40; `ProxyAction.cs:577-599`) flips the source action's `enabled`
(`ProxyAction.cs:303-307`). Keyboard mod actions are also masked while a text field has focus or an overlay is up
(`InputManager.GetMaskForControlScheme`, `InputManager.cs:644-652`, feeding the pre-resolved mask in `ProxyAction.UpdateState`,
`ProxyAction.cs:524-575`). Two mods on the same key are never resolved against each other, so both fire.

## Probe and watcher

The one-shot probe `ProbeDelayFrames` after arming WARNs keyed on `!action.enabled` (the runtime truth) and logs, as INFO, each
binding's options marker: `ProxyBinding.hasConflicts` / `.conflicts` (`ProxyBinding.cs:414-456`, 458-501) test every action sharing
the key path with `ConflictsWith(checkUsage: TRUE)` whether or not that action is enabled (that is what draws the options row's
triangle), while the runtime pass uses `checkUsage` FALSE against ENABLED actions only. The two can disagree, so the log labels each
"options marker" or "runtime". The probe runs under whatever tool is active 120 frames after the load (normally the default tool); a
per-frame watcher (four bool reads) therefore logs when an armed action turns non-live, with the active tool's id
(`ToolBaseSystem.toolID`, `ToolBaseSystem.cs:142`), and the matching return to live.

The watcher re-arms. Any text-field focus masks every keyboard mod action (`UserInterface.cs:170` sets
`InputManager.hasInputFieldFocus`; `RefreshActiveControl` runs at the end of each input update (`InputManager.cs:1395`) and sets the mask
to Mouse only (:644-652); the mask setter pushes it to every map (:441-446) -> `ProxyActionMap.mask` -> `ProxyAction.UpdateState`
(`ProxyActionMap.cs:52-70`, `ProxyAction.cs:524-575`) -> the next conflict pass disables the source action
(`InputConflictResolution.cs:14-24`, 37-40, 187-190)). A once-per-load latch would be used up by the first save-name box and a later
real conflict in another tool would go unlogged while the key was dead. So every drop is logged with a cause label read at that
moment: "text field / overlay mask" when `InputManager.hasInputFieldFocus` (:325) or `.overlayActive` (:341) is true (public getters),
else "probable key conflict in tool X - or a mask this mod cannot read" (the key-rebinding capture sets the internal
`blockedControlTypes`, `InputRebindingUISystem.cs:155-158` / `InputManager.cs:450`, 651, and UI input barriers are not queryable;
reflection is banned, so these are labels, never filters). Each drop line arms one matching "live again" line. Lines are capped per
action AND per cause label at `WatcherLinesPerAction` per load, then ONE "further drops not logged" line. The dev-tab `StatusLine` is
refreshed on "live again" too.

Whether vanilla binds Ctrl/Shift/Alt+G anywhere is input-asset data: unverified (every live probe so far showed the G family live).

## Defensive contract

House pattern (`SkiRunOwnershipSystem`): "created" line, ARMED line per load, `m_Armed` / `m_Disabled` / `m_ErrorLogged` reset in
`OnGameLoadingComplete`, one-shot "!!!" catch that disables the hotkeys until the next game load. Game mode only.
`GameManager.isGameLoading` guard (`GameManager.cs:262-272`): UIUpdate keeps ticking while a save loads and the tool is mid-reset
(`ToolSystem.PreDeserialize` forces the default tool, `ToolSystem.cs:610-614`). No per-frame allocation: strings are built only when
a line is logged.
