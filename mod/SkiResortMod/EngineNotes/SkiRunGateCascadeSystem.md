# SkiRunGateCascadeSystem: why a deleted Welcome Sign takes its runs with it, and why at Modification2

When a sign (a `Building`) is deleted, the ski runs bound to it are deleted by our own code.

## Why our own cascade

Runs carry no `Owner` and the sign carries no `SubArea` buffer, so every vanilla cascade ignores the relation:
`SubAreaReferencesSystem` is Owner-gated (SubAreaReferencesSystem.cs:122-134), `SubElementDeleteSystem` and the bulldozer's own walk
read the sign's SubArea buffer (SubElementDeleteSystem.cs:66-82; BulldozeToolSystem.cs:907-935). The link is `SkiRunBinding.m_Gate`,
so this system walks it.

## Why Modification2

Mod.cs: `UpdateBefore<SkiRunGateCascadeSystem, SkiGladeCleanupSystem>(Modification2)`.

* Bulldozed: `ApplyObjectsSystem.Delete` adds `Deleted` at ApplyTool inside ToolUpdate (ApplyObjectsSystem.cs:484-488).
* Collapsed: `CollapsedBuildingSystem` adds `Deleted` through the EndFrameBarrier (CollapsedBuildingSystem.cs:150-175, :261, :318), which
  plays back at the start of MainLoop (SystemOrder.cs:49).
* Both are visible at Modification2: MainLoop runs ToolSystem then ModificationSystem (SystemOrder.cs:58, 60), which runs
  Modification1 -> ... -> ModificationEnd in order (ModificationSystem.cs:19-26); Modification1/2 hold no vanilla area-delete consumer
  (SystemOrder.cs:94-121). `Deleted` lives one frame: `PrepareCleanUpSystem` and `CleanUpSystem` (SystemOrder.cs:50, :54) destroy the
  sign, its runs and their trees in the same Cleanup.
* Anchored BEFORE `SkiGladeCleanupSystem` so the glade cleanup sees the runs deleted here the same frame and their member trees go
  with them. `UpdateBefore` on a mod system registers into the anchor's reference list (UpdateSystem.cs:168-176, :278-292). The runs'
  border props are Secondary sub-objects, deleted by `SecondaryObjectSystem` at Modification4B for a `Deleted` owner
  (SecondaryObjectSystem.cs:728-740; SystemOrder.cs:183), not by `SubObjectSystem`.
* Not on `Destroyed`: a disaster keeps the sign entity, so its runs stay bound. Only `Deleted` is a delete.

## Shape of the delete

Copied from `SkiGladeCleanupSystem.DeleteLikeOwnerCascade` (mirrors SubElementDeleteSystem.cs:78-79): strip Applied/Created/Updated
where present, add `Deleted`. No refund (vanilla never refunds areas, ApplyAreasSystem.cs:158-165).

## Queries and guards

* `m_DeletedGates` = All{Building, Deleted, PrefabRef} None{Temp}. Temp excludes the bulldozer's preview copy and an applied Temp,
  which also end `Deleted`.
* `m_BoundRuns` = All{SkiRunBinding, Area} None{Deleted, Temp}.
* Guards per frame: `m_Disabled || !m_Armed`; `GameManager.isGameLoading`; `ownership.PrefabSetsReady`, not `ownership.IsArmed`: an
  exception in the sweep or the load pass must not switch the cascade off (degraded mode stays "runs go with their sign").

## Cost

One `IsEmptyIgnoreFilter` per frame. When any building is deleted, a managed scan of the deleted buildings' `PrefabRef`
(`ToComponentDataArray` completes only PrefabRef's writers). Only when one is a sign of ours, one copy of the bound runs' bindings
(main-thread EntityManager batch calls, a sync point on that rare frame).

## Failure mode and logging

A one-shot "!!!" disables the cascade until the next load. Runs whose sign is deleted meanwhile are NOT deleted: the ownership sweep
reports them as paid orphans ("[orphan]" WARN); they are never deleted on an inferred event.

Game mode only; "created" line; ARMED line per load listing every constant; `m_Armed`/`m_Disabled`/`m_ErrorLogged` reset in
`OnGameLoadingComplete`; one log line per deleted sign (zero runs included), so silence is impossible.
