# Settings/LocaleEN: which locale keys the options UI and key bindings read

`Settings/LocaleEN.cs` is the English source for the options UI (`SkiResortSettings`). `Mod.OnLoad` registers it with
`localizationManager.AddSource("en-US", new LocaleEN(settings))` next to the prefab and hub texts of `LocaleEN.cs` (see `LocaleEN.md`). Text
is plain characters only (°, &, %).

## Key-binding keys
Every key is built with the `ModSetting` helpers (ModSetting.cs:303-371):
- Option row label / description: `GetOptionLabelLocaleID` / `GetOptionDescLocaleID(property)`. The KeyBinding widget uses the normal item
  displayName / description (AutomaticSettings.cs:1492-1500).
- `GetBindingKeyLocaleID(action)` gives `Options.OPTION[<id>/<action>/<binding>]` (ModSetting.cs:343-361) and
  `GetBindingKeyHintLocaleID(action)` gives `Common.ACTION[<id>/<action>]` (ModSetting.cs:363-366). The frontend rebinding
  UI and the input hints consume them, not Game.dll C# (which screens show them: unverified).
- `GetBindingMapLocaleID()` gives `Options.INPUT_MAP[<id>]` (ModSetting.cs:368-371), the map title the conflict notification falls back to
  (InputManager.cs:1522).

The binding plumbing itself (registration, defaults, conflicts) is in `SkiResortSettings.md`.
