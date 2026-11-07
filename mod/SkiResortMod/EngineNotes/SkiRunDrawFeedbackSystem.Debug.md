# SkiRunDrawFeedbackSystem.Debug: how the "Ski Runs" developer-menu tab is registered and why its rows are cheap

## Registration

`DebugSystem.RegisterDebug` invokes each `[DebugTab]` method of every `[DebugContainer]` type (DebugSystem.cs:1026-1045). A tab
method declared on a `ComponentSystemBase` runs on the LIVE system instance (`ExecuteMethod`, DebugSystem.cs:996-1024;
`GetOrCreateSystemManaged` at :1006-1009). That is why the debug tab is a partial of the system. Nothing is registered in Mod.cs.

## Tab name

The tab name must be unique ("Ski Runs", not "Ski Resort" or "Ski Glades"). `AddPanel` keys widget lists by tab name and removes
the list previously registered under the same name (DebugSystem.cs:1085-1095, `Rebuild` at :971).

## Cost

`DebugUI.Value` getters run at 10 Hz while the tab is open. So every row reads a plain field of this system, of `SkiRunContourSystem`
(`StateText`) or of `SkiRunBindTriggerSystem` (`LastReason`, `Triggers`): no EntityQuery, no job completion, no allocation beyond the
display string. Each tab build resolves the two sibling systems once with `World.GetExistingSystemManaged`. It returns null when a
system is not registered, and the rows then say so. Heavier work (`DumpToLog`) runs on a button press.
