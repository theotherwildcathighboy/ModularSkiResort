# SkiRunBindTriggerSystem: why runs bind at ModificationEnd, and how signs get auto-named

`SkiRunBindTriggerSystem` binds a ski run the frame it is drawn, and waiting runs (unbound runs and paid orphans) the frame a Ski Area
Welcome Sign is placed, paused or not. It owns no binding logic: on a qualifying frame it calls `SkiRunOwnershipSystem.SweepNow`, the
same idempotent sweep (`SkiRunBindingRules` table) the 64-sim-frame cadence runs. Setting off = exactly the cadence-only behavior.

## Frame anatomy

Decompile baseline 1.6.0f1; 1.6.2 shifts noted.

The click that finishes a run runs `ApplyAreasSystem.Create` inside ToolUpdate's ApplyTool phase (ApplyAreasSystem registered at
SystemOrder.cs:717, 1.6.2 :718; ToolOutputSystem.cs:27-28): the Temp entity is kept, Temp removed, Applied + Created + Updated added
(ApplyAreasSystem.cs:204-208, :272) through the ToolOutputBarrier (:297-299), which plays back inside ToolUpdate (SystemOrder.cs:694-695,
1.6.2 :695-696), i.e. before ModificationSystem in the same MainLoop (SystemOrder.cs:58, 60). GeometrySystem re-triangulates every
Area+Updated at Modification2B (SystemOrder.cs:124; GeometrySystem.cs:752, 780), so `Geometry.m_CenterPosition`, what the sweep's tether
test reads, is final by ModificationEnd. `Created` lives until Cleanup, after the whole MainLoop (CleanUpSystem.cs:53); the
GameSimulation sweep never sees it. In game mode a freshly drawn area has no Owner (AreaToolSystem.cs:1928, 2108-2111) and our runs never
get one either: the link is `SkiRunBinding`.

## Why ModificationEnd

Registration is in Mod.cs. MainLoop runs paused or not; ModificationSystem runs ModificationEnd last (ModificationSystem.cs:19-26). Mods
register after SystemOrder.Initialize (GameManager.cs:615 CreateSystems -> :2381, then :618 InitializeModManager; 1.6.2 :621/:2396/:624),
and a phase is sorted by add index (UpdateSystem.cs:39-47, 158-161), so a plain `UpdateAt` lands after every vanilla `UpdateAt` of
ModificationEnd (ValidationSystem :264 included) and before its `UpdateAfter` members (ModificationEndBarrier :93, TerrainSystem :303,
1.6.2 :304). Every vanilla Modification system has therefore already processed the frame; `SkiRunBinding` is a structural change that
raises no tag (no Updated/BatchesUpdated), so vanilla systems still see nothing, exactly as with the cadence sweep. No update interval:
ModificationSystem calls the non-indexed `UpdateSystem.Update(phase)`, which ignores intervals (UpdateSystem.cs:183-221 vs :223-241).

## Queries

- `m_NewRuns`: All{Area, Geometry, PrefabRef, Created} None{Temp, Deleted, Owner, SkiRunBinding}. Edits never re-create a run
  (ApplyAreasSystem.cs:123-131, 167-202: Node copy + Updated on the original, which keeps its binding), so an edit never fires this.
  Owner stays in None: it keeps vanilla owned lots (every new building's lot is a Created area with Owner) out of a query that would
  otherwise match on most building-growth frames.
- `m_ChangedGates`: All{Building, Transform, PrefabRef, Created} None{Temp, Deleted}: Created only. A placed sign is the former Temp
  entity with Applied/Created/Updated (ApplyObjectsSystem.cs:670-686). An Any{Created, Updated} arm would fire SweepNow on nearly every
  frame of a sign relocate (>= 128 full scans of every building and run in ~1.5 s; which vanilla writer stamps Updated every frame is
  unverified). Cost of not having it: unbound runs (and paid orphans) brought into tether by a relocated sign bind on the next unpaused
  64-frame sweep instead of at once.

Both match vanilla entities too (`m_NewRuns` any new un-owned area, e.g. a vanilla surface the player draws; `m_ChangedGates` every new
building), so the result is filtered in managed code against our prefab sets; `IsEmptyIgnoreFilter` keeps idle frames free of any job
sync.

## Safety

SweepNow writes from the main thread: EntityManager structural changes and Get/SetComponentData complete running jobs (Entities
semantics, unverified here); the sweep completes both of its queries' dependencies first. Double charge is impossible: BindCharge is
decided only for a run without a binding, and the sweep adds the binding synchronously right before the one Charge call, so the cadence
sweep and this trigger can never charge the same run twice. Charging while paused matches vanilla (ToolApplySystem charges PlayerMoney
from an ApplyTool job in MainLoop, ToolApplySystem.cs:68-72, scheduled :135).

## Load-pass retry

See the `SkiRunOwnershipSystem` header, load-pass failure. After a load pass that threw, ownership keeps its sweep running in a
no-charge mode and raises `LoadPassRetryPending`; this system, the only ownership caller that ticks on the first frame after a load,
paused or not, then calls SweepNow once ("load-pass retry"), whatever the bind-on-draw setting, so runs still carrying Owner(sign) lose
the one-run-bulldoze trap on that frame instead of at the first unpaused 64-frame sweep. Any sweep clears the request. One bool read per
frame.

## Sign auto-name

This system already sees placed signs (`m_ChangedGates`, Created only), so it also names them: a Created, non-Temp Welcome Sign with no
custom name, in Game mode, after the load is complete, gets "Ski area N" (`SkiResort.Hub.RESORT_DEFAULT` from the active dictionary,
vanilla precedent NameSystem.cs:250; English "Ski area {N}" when the key is missing) with N the smallest integer >= 1 such that no live
sign's current custom name equals that text.

The write is `NameSystem.SetCustomName` (Game.UI/NameSystem.cs:214-241, same in 1.6.2): it stores the name in NameSystem's dictionary at
once and adds CustomName + BatchesUpdated through NameSystem's own EndFrameBarrier command buffer (the vanilla title field's call,
TitleSection.cs:40, :48-52), so the vanilla panel and the hub show one name, it is saved with the city (:549-575) and it stays a plain
string after an uninstall. Names are stored already localized (a later language change does not rename them). One line per name:
`Resort [name] new Welcome Sign 124274:35 named 'Ski area 1'`. Independent of the bind-on-draw setting (naming is not binding). Cost: on
a frame where a building is Created, the PrefabRef scan `AnyGatePrefab` already does, and only when a new sign is among them a scan of
every live building for the current sign names (event time).

### Written at UIUpdate, not here

`SetCustomName` calls `EndFrameBarrier.CreateCommandBuffer` (NameSystem.cs:237), and that throws at ModificationEnd:
`SafeCommandBufferSystem.CreateCommandBuffer` refuses while `m_IsAllowed` is false (Game/SafeCommandBufferSystem.cs:16-23), EndFrameBarrier
clears it in its own OnUpdate (:25-30) as the first MainLoop system (Game.Common/SystemOrder.cs:49) and only
`AllowBarrier<EndFrameBarrier>` (:62) re-allows it, after ModificationSystem (:60), which runs ModificationEnd. Same order in 1.6.2
(`LiftParkSystem` hits the same trap). So this system only picks the name and queues it; `SkiSignNameSystem` (UIUpdate, after :62;
UIUpdateSystem :67) calls SetCustomName the same frame. A queue still full at the next ModificationEnd means the writer is not running:
one "!!!" line per load.

### The pick is shared

`SkiResortAggregate.SmallestFreeOrdinal` (pure, tested in tests/ResortBatch) picks N here and in `PickSignName`, which the hub calls when
the player renames a sign to "": the hub then writes a fresh "Ski area N" instead of removing the name, so the vanilla panel and the hub
keep showing one name (removing it made the panel show the vanilla default while the hub showed a positional label, and that label could
equal another sign's auto-name).

### Load guard

Whether loaded entities still carry Created on the first frames after a load is unverified; a sign Created within
`AutoNameLoadGuardFrames` updates of the load completing is not named (one line says so): a player cannot place a sign that fast, and an
old unnamed sign keeps the hub's display-only fallback label instead of being renamed by a load.

## Defensive contract

House pattern (`SkiRunOwnershipSystem`): "created" line, ARMED line per load, `m_Armed` / `m_Disabled` / `m_ErrorLogged` reset in
OnGameLoadingComplete, one-shot "!!!" catch that disables the trigger until the next game load (the cadence sweep keeps binding). Game
mode only. `GameManager.isGameLoading` guard (GameManager.cs:262-272, 1.6.2 :264-274).

## Near SkiSignNameSystem

Registration (Mod.cs): `UpdateAt<SkiSignNameSystem>(SystemUpdatePhase.UIUpdate)`, added before `SkiRunCatalogSystem` /
`SkiResortStatsSystem`; the reasons are in the comment on the class. Its house rules: "created" line; one ARM line per load; Game mode
only; one-shot "!!!" catch that disables the writer until the next load (the bind trigger then reports the stalled queue once). Per
frame: one int read; nothing allocated.
