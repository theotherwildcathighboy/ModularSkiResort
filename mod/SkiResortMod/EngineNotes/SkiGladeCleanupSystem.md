# SkiGladeCleanupSystem: how the game cascades Deleted to owned sub-objects, and where our tree cascade hooks in

Decompile baseline `Game.dll` 1.6.0f1 (1.6.2f1 line in brackets where it moved).

## Why our own cascade

Planted trees have no Owner (see the `SkiRunTreeMember` header), so vanilla's owner cascade cannot see them. This mirrors its shape:
`SubObjectSystem` processes a Deleted owner (`m_UpdateQuery`, `SubObjectSystem.cs:3936-3944` [3944-3951]) and
`RemoveUnusedOldSubObjects` (:1091, body :1227-1268 [1230-]) gives every owned non-Building, non-Secondary, non-ServiceUpgrade
sub-object `RemoveComponent(Applied, Created, Updated)` + `AddComponent(Deleted)` (:1241-1242 [1244-]; type set :3967 [3975]): plain
Deleted, no Temp, no refund. We do the same to member trees. A member tree with sub-objects of its own (unverified that any tree
prefab has them) needs nothing extra: `SubObjectSystem` picks up a Deleted entity with a `SubObject` buffer later this frame
(Modification2B, `SystemOrder.cs:131`).

## Timing (why Modification2)

A direct bulldoze / area-tool delete / info-panel Delete adds Deleted to the run at ApplyTool (`Game.Tools/ApplyAreasSystem.cs:158-165`);
a run carries no Owner, so the bulldozer reaches the run itself, not the sign. A sign delete goes through `SkiRunGateCascadeSystem`
(Modification2, UpdateBefore this system), which adds Deleted to every run whose `SkiRunBinding` points at the sign, on the main
thread, visible to our query in the same pass.

Tool paths land inside `ToolSystem` (MainLoop, `SystemOrder.cs:58`) and the Modification phases run after it in the same frame
(`ModificationSystem`, `SystemOrder.cs:60`; `Game.Common/ModificationSystem.cs:19-26`), paused or not. Deleted lives one frame
(`PrepareCleanUpSystem` after MainLoop, `CleanUpSystem` at Cleanup, `SystemOrder.cs:50` / :54), so a member tree Deleted at
Modification2 is destroyed in the same Cleanup as its run.

## Cost

Per frame one `IsEmptyIgnoreFilter` check (deleted areas). On a frame where any area is Deleted the deleted areas are walked once and
filtered on our run prefab set; only when one of our runs is among them is a line written, and the member scan runs only when a
member exists. Deleting a run is rare, so main-thread EntityManager batch calls (a sync point) are fine. Plus one full member scan
per load (orphan sweep + census line).

## Orphans

`m_Run == Null`, `!Exists(m_Run)` or no longer an Area (e.g. a run deleted while this system was disabled): the tag is stripped and
the tree stays as a plain tree.

## Defensive contract

Game mode only; run prefabs resolved with the tolerant PrefabID + name-scan resolver, cached per session; ARMED line per load;
`m_Armed` / `m_Disabled` / `m_ErrorLogged` reset in `OnGameLoadingComplete`; one-shot "!!!" catch that disables the cascade until the
next load (member trees then outlive their run; the next sweep strips the dangling tags).

## Filler tags

A `LayerFiller` tag with `m_Run == Entity.Null` (a Quick runs filler tree) is never an orphan: neither the load sweep nor the cascade
strips it, and no run delete reaches it. The load census reports them as "filler N".
