# SkiGladeToolSystem.Debug: how the "Ski Glades" developer-menu tab is wired

`SkiGladeToolSystem.Debug.cs` is the "Ski Glades" tab of the game's developer menu (the ChairPinSystem.Debug.cs pattern).

## Registration

`DebugSystem.RegisterDebug` sweeps every `[DebugContainer]` type and invokes each `[DebugTab]` method (DebugSystem.cs:1026-1045).
A tab method declared on a ComponentSystemBase runs on the live system instance (ExecuteMethod, DebugSystem.cs:996-1024;
GetOrCreateSystemManaged :1006-1009), which is why this file is a partial of the tool. Nothing is registered in Mod.cs.

## Tab name

The tab name is load-bearing: "Ski Glades", not "Ski Resort". AddPanel keys widget lists by tab name and removes the previously
registered list under the same name (DebugSystem.cs:1085-1095), so a second "Ski Resort" tab would wipe ChairPinSystem's tab
(ChairPinSystem.Debug.cs:50), or the reverse, depending on reflection order.

## Cost

As in ChairPinSystem.Debug: `DebugUI.Value` getters run at 10 Hz while the tab is open, so every row reads a plain field or a
trivial property: no EntityQuery, no job completion, no allocation beyond the display string. Heavier work (DumpToLog) runs on a
button press and goes to the mod log.

## Tool activation from the panel

Activation from here is the route vanilla UI uses: `TryActivate` sets ToolSystem.activeTool (ToolSystem.cs:84-99;
ToolUISystem.cs:285-304); the swap lands in the next ToolUpdate (:312-327). While the cursor is over this panel, Tool-map mouse
actions are barrier-blocked (ToolSystem.cs:203-222) and controlOverWorld is false: after clicking a button, move the cursor onto
the map before Apply works.

## Rows

- Species pool: an optional void partial, `DescribeSpeciesPoolForDebug`, is implemented by SkiGladeToolSystem.cs from
  m_SpeciesPoolText ("(unresolved)" before the tool first arms, "(none)" if no species resolved).
- BlockedStatus ("Apply blocked by N error(s)" / "-") is published by the tool's hold path while a validated preview holds Error
  entities.
- Doomed-tree hiding button: if Hidden on the felled / replaced Temp copies misbehaves in the field, this switches the after-state
  hiding off for the session (doomed trees stay visible, rings grow to Tuning.FallbackRingDiameter) and back on, no rebuild
  needed. The tool logs every switch.
