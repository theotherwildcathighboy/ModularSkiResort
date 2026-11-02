# SkiResortSettings: option design, density polling and key-binding plumbing

Options live under Options > Mods > Modular Ski Resort. Every balance constant has a setting so players can fix tuning mistakes
without waiting for a patch. Property names are persisted: never rename them (labels may change). The `Default*` constants are the
one copy of each default: `SetDefaults` and every null-settings fallback use them. Readers are named on each property; systems read
through `Mod.Settings?.X ?? DefaultX` (or a clamped `*Of` reader) because `Mod.Settings` is null before `OnLoad` and after
`OnDispose`.

## Run outline

`RunOutlineSize` is only the thickness of the overlay `SkiRunOutlineSystem` draws over a run while it is drawn, edited or
bulldozed. It does not touch `AreaGeometryData.m_SnapDistance`, so snapping (0.75 m) and the minimum corner distance (0.375 m) are
the game's.


## Ski days

The Default / Min / Max constants alias `SkiDayRules`' (one source; tests/ResortBatch links `SkiDayRules.cs`, not this file).
`SkiDayReachKm` uses unit "integer" with "(km)" in the label (Game.UI/Unit.cs:5) because "length" would read meters.
`SkiDayRules.ClampKnobs` is a second, defensive clamp. Lodge spend constants alias `SkiCatalog.LodgeSpend`.

## Key bindings

Key bindings are plain `ModSetting` members (no `UIInputActionCollection`, no reflection).

- `ModSetting` discovers only public get+set properties typed `ProxyBinding` (ModSetting.cs:32-34). Its constructor generates each
  default binding from the `[SettingsUIKeyboardBinding]` attribute (ModSetting.cs:43, 56-64; action name =
  `attribute.actionName ?? property name`, :78; SettingsUIKeyboardBindingAttribute.cs:41, 119-136, ctor :153-160). So `SetDefaults`
  must not assign them: that would replace the generated binding with an empty `default(ProxyBinding)`.
- The class-level `[SettingsUIKeyboardAction(name)]` gives each action's keyboard composite the default usages
  (SettingsUIKeyboardActionAttribute.cs:9; SettingsUIInputActionAttribute.cs:41-51, read by `RegisterKeyBindings` at
  ModSetting.cs:150-153, 183-192). Without it the composite carries no usages (ModSetting.cs:195-197) and the options row never
  shows a conflict warning.
- The actions only exist once `Mod.OnLoad` has called `RegisterKeyBindings()` (ModSetting.cs:141-231); before that `GetAction`
  returns null (:289-292). Mod actions start disabled (ProxyAction.cs:322-349); `SkiGladeHotkeySystem` enables them per mode.
- A mod action always loses to an enabled vanilla system/UI action and is silently disabled (InputConflictResolution.cs:118-190;
  mods are never built-in, ModSetting.cs:181). Defaults therefore avoid camera keys. Whether vanilla binds Ctrl/Shift/Alt+G is
  input-asset data, not code (unverified); the options row's conflict triangle answers it in the field.

### Default chords

- Glade toggle is Ctrl+Shift+G (plain Ctrl+G showed a conflict marker, likely from two installed mods' defaults, unverified). A saved
  rebind survives; whether a changed default reaches players who never rebound is unverified.
- `DumpDiagnostics` is Ctrl+Alt+G. Every live probe logged ctrl/shift/alt+G all live, so no vanilla binding on G disallows
  modifiers (`PathEquals` uses the path-only comparer when either side disallows them, ProxyBinding.cs:822-825; the runtime pass,
  InputConflictResolution.cs:148-182, disables the losing mod action).
- Ctrl+Alt+G vs our Alt+G: an Allow-modifiers composite gets a prohibition processor for every supported modifier the binding does
  not use (InputManager.cs:2104-2112), so Alt+G should not fire on Ctrl+Alt+G (Unity-side behavior unverified).
- Ctrl+Shift+D was rejected: D is vanilla's camera-right key and a press would pan the camera off the run being dumped.

`QuickRunsEnabled` (Runs group, default OFF since DECISIONS 443): the experimental Resort Planner tool (code name Quick runs). Off = the tool cannot open, its hotkey is inert, the hub chip is grayed with the reason (QuickRunsToolSystem.OptionOn). Key binding ToggleQuickRunsTool, default Ctrl+Shift+Q.
