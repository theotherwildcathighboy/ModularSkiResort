# SkiResortHubUISystem: bindings, triggers, selection rules, launch and camera behavior of the hub's C# side

The C# side of the Ski Resort hub: every binding and trigger of the "skiResort" group. The TS side is
mod/SkiResortUI/src/bindings.ts, the single source of truth for names and shapes; mod/SkiResortUI/tools/check-bundle.js matches
this file's binding/trigger literals and every `writer.TypeBegin("SkiResortMod.<X>")` block against it (rules R1, R2); the build
items are the `SkiCatalog.cs` BUILD-ITEMS table, which gate R16 reads. Also here: the selected resort, rename through the vanilla
`NameSystem`, the build launches, the camera fly-to for runs, lifts and buildings, the glade-tool controls, auto-open on Welcome
Sign selection, the `uiHello` handshake and what the piste-map overlay (`SkiPisteMapSystem`) reads. The per-area numbers are not
computed here: `SkiResortStatsSystem` owns them and this system only writes its rows (resorts / resortLive / runs / runLive /
lifts / problems / city) to the UI.

## Bindings

Value bindings `hubOpen`, `contract`, `buildStamp`, `showHubButton`, `build`, `glade`, `resorts`, `resortLive`, `selectedResort`,
`runs`, `runLive`, `lifts`, `problems`, `city`, `pisteMap`, `follow` and `history`, plus the triggers listed in bindings.ts. The slow
rows (resorts, runs, lifts) and the live rows (resortLive, runLive) are separate arrays so a snow, guest or money change re-sends
only the small live arrays; `problems` is built in C# (`HubProblems.Build`). `pisteMap` (on at every load) and `follow` (a counter,
+1 per rule-7 switch) are ValueBindings that do not drive Demand. `hoverRun` is a field write (no queue: mouse traffic must not
fill `QueueCapacity`), validated against the catalog in the next OnUpdate and counted; only its first valid and first refused call
per load are logged.

## History binding

`history` is a `RawValueBinding` without Demand (only the History view subscribes it). One `HistoryArea` row per live ski area (the
stats `resorts` order, Not linked excluded: it has no sign and no history): `sign`, `firstMonthKey` (`SkiMoneyRules.MonthKey`: year
x 12 + month 0..11) and 15 parallel arrays of the same length n, index i = month firstMonthKey + i, oldest → newest.

Built from the sign's `DynamicBuffer<SkiResortHistoryMonth>` densified here (a Gap marker covers its key .. the next element's key
- 1: every month of it is sent with flags GAP, flows 0, unknowns -1), then the ledger's month in progress appended last with flags
CURRENT (128, binding only, never stored) | PARTIAL when the ledger counts it since load; months between the last stored one and it
are GAP. The month in progress: the guests from the ledger's This (booked so far), `SkiDaySystem`'s `ArrivedThisMonth`, and its
eight money terms = the ResortLive row's Net rows (`ResortLiveStats.NetTerms`: booked so far, buildings / wages = the rate x the
share of the month simulated), so its net is the Finances tile's `netMonth`; the ledger's This only when that row has no terms yet
(-1, the counter not armed at the last rebuild); attraction and snow % = the ResortLive point values now. snowPct -1 = unknown (the
stored 255).

Pushed only while observed and only when `SkiResortHistorySystem.Revision` moved (a stored month), the area set (stats
`ResortsRevision`) moved, or `ProgressRevision` moved AND `HistoryProgressPushS` (5 s wall clock) passed since the last push;
re-sent once on (re)subscribe. Payload ~ 15 numbers x months x areas (5 years x 5 areas ~ 4,500 numbers).

## Binding mechanics

All registered with `AddBinding`, never `AddUpdateBinding`, which would re-send a raw binding every frame (UISystemBase.cs:48-58).
`Colossal.UI.Binding` facts (decompile of the installed 1.6.2f1 DLL): `IJsonWriter` has Write(bool/int/uint/long/ulong/float/
double/string), `ValueBinding<T>(group, name, initial, IWriter<T> writer = null, EqualityComparer<T> comparer = null)`.

- `ValueBinding<T>.Update` pushes only when its comparer reports a change (ValueBinding.cs:37-44) and only while JS observes it
  (`TriggerUpdate` checks `active`, :46-53); a subscribe pushes the current value at once (`OnSubscribe`, :24-35).
  hubOpen/contract/buildStamp/showHubButton are bool/int/string (ValueWriters.cs:14-36); `glade` is a struct implementing
  `IJsonWritable` + `IEquatable` (scalar fields, no boxing); `selectedResort` is `ValueBinding<Entity>` (precedent
  UpgradeMenuUISystem.cs:130, SelectedInfoUISystem.cs:146); `city` is `ValueBinding<CityStats>` with our writer and comparer
  (`CityStatsWriter` / `CityStatsComparer`), so the dedup and the TypeBegin name do not depend on how `SkiResortStatsSystem`
  declares the struct.
- `RawValueBinding` re-sends the whole payload on every `Update()` while observed, no dedup (RawValueBinding.cs:47-56), and on
  subscribe (:22-33). The six arrays resorts / resortLive / runs / runLive / lifts / problems are pushed only when their own
  revision of `SkiResortStatsSystem` moved (content-hash gated by the stats system) and re-sent once on (re)subscribe; `build` when
  a signature of every field it sends changed. A push whose row count changed writes `Hub [push] <name> rows=...` through that
  array's `LogWindow`(10 s) (`problems` follows simulation state, e.g. a factor flipping around 100 % in a brownout); folds are
  flushed on the next `PushArray`, in `OnGamePreload` and in `OnDestroy`.
- `observerCount` / `active` are public (EventBindingBase.cs:14-16): every binding except hubOpen / contract / showHubButton /
  buildStamp is subscribed only by panel-mounted components (bindings.ts header), so "observed" = "the panel is open". This system
  sets `SkiRunCatalogSystem.Demand` and `SkiResortStatsSystem.Demand` every frame to "any of build, resorts, resortLive, runs,
  runLive, lifts, problems, city observed" (pisteMap and follow do not demand): nothing is computed for the panel while it is
  closed (the catalog's rating service runs anyway).
- Rows are typed objects written token by token (TypeBegin / PropertyName / Write; JsonWriter.cs, one native call per token, no JSON
  string). Every TypeBegin name is "SkiResortMod.<TS interface>" (the UI ignores `__Type`). An Entity is written {index, version}
  (UnityWriters.cs:17-25) and `TriggerBinding<Entity>` reads that shape back (UnityReaders.cs:19-27). Numbers go through `Fin()`: a
  non-finite float or double is sent as 0 and counted (one WARN per load): one NaN must not break the UI's JSON.
- **One shape per TypeBegin name (engine fact, in game, R3 DECISIONS 578).** Objects written under the same TypeBegin name must carry
  the same properties in the same order. R3 first wrote the Run tile's eight step fields (`naturalPrice` ... `lightsMonthlyPerHa`) on
  the Run BuildItem only: the game process exited the frame the hub opened, with no log line, no crash report and no WER entry
  (bisected over 8 launches: drawRun alone was fine, the conditional fields alone killed it). Fix: every BuildItem writes every field,
  -1 where it does not apply, and the UI tests `>= 0`. Never put a PropertyName inside a condition in a TypeBegin writer.
- Every number of the stats structs is written through the `Fin` overloads (int / float / double) and enum-like fields (kind, state,
  band, season) are cast to int, so this file compiles whatever numeric type the stats structs pick.
- A trigger exception is caught by the binding and logged to the vanilla UI log, not ours (TriggerBinding.cs:21-31, :58-69, :87-99),
  so every trigger body here has its own try/catch and "!!!" line.

## Triggers

Every body: try/catch; Enabled check (a `UISystemBase` is disabled outside its gameMode at `OnGamePreload`, UISystemBase.cs:60-64,
but its bindings stay attached until `OnDestroy`, :29-37, so triggers still arrive); an existence check on any entity argument (a
Null argument is allowed only for `selectResort` and the `top` of `focusLift`); the request is QUEUED and applied in OnUpdate (main
thread, UIUpdate), which writes one log line per trigger. Exceptions: `uiError` (once per distinct message) and `uiLayout` (only
when the string changed). C# never gates a trigger on the UI's contract: a bundle of another contract still launches tools.

## Selected resort

`m_SelectedResort` is not saved: a live Ski Area Welcome Sign, or `Entity.Null` = the "Not linked" view. Rules, each with one
"Hub [select]" line:

1. Load: Null; at the first stats rebuild of the load with >= 1 sign: the first sign in stable order.
2. World selection change (one field compare per frame): the mapping runs BEFORE the auto-open early return. Our sign → it; an
   entity whose Owner is our sign (the lodge: vanilla ServiceUpgrade buildings carry Owner = the sign) → the owner; our station →
   the resort of the first census row touching it (resolved from that row's base station, else its top station), a station on no
   row → the resort of its own position (`SkiResortStatsSystem.TryResolveResort`: one rule); anything else → unchanged, no line.
   The line names what was selected.
   Known limit: clicking the sign that is already selected in the world does not switch the hub back to it after the player used
   ‹ ›: `DefaultToolSystem.SelectTempEntity` writes `ToolSystem.selected` only when the hit entity or index differs
   (Game.Tools/DefaultToolSystem.cs:1176-1189, same in 1.6.2; toggleSelected comes from the cancel action, :903), so the per-frame
   compare sees no change. Detecting the re-click would need the tool's Apply action: `ToolBaseSystem.applyAction` is protected
   (ToolBaseSystem.cs:188) and `InputManager.toolActionCollection` internal (InputManager.cs:536), not reachable without
   reflection; the "Tool"/"Apply" ProxyAction names are asset data, unverified. Workaround: click elsewhere first, or use ‹ ›.
3. `selectResort(e)`: a live sign, or Null (refused while the Not linked row is empty). An accepted switch from the UI also moves
   the camera (see Switcher below).
4. A sign that was not in the previous resort rows → selected ("new sign": covers placed from the hub).
5. The selected sign is no longer live → the first remaining sign, else Null.
6. Null selected and the Not linked row is empty → the first sign (stays Null with no sign).
7. The hub follows what you build: a run that newly bound, a station newly placed or a lift that newly closed, in a live ski area
   other than the shown one → that area, through `HubRules.FollowCheck`'s guards: only while the hub is open, never in a pass where
   rule 1/4/5/6 already switched, not within `FollowQuietAfterUserS` (2 s) of the player's own switch (rules 2 and 3 stamp it), at
   most one follow per `FollowMinIntervalS` (2 s). One candidate per pass, the most deliberate act first (a lift closed, then a
   station placed, then a run bound); the follow writes one "Hub [follow]" line instead of the "Hub [select]" line, bumps the
   `follow` binding (the UI's "Now showing ..." notice), never moves the camera and never ends a lodge on the cursor. A suppressed
   follow writes nothing (the bind / lift notice of the pass names the event) and is counted per reason (DUMP `hub v3:`). Builds
   made while the hub was closed are seen at its next open (one follow).

Switching resort in the hub never selects anything in the world. Rules 1, 4, 5, 6 run only when `ResortsRevision` moved, rule 7's
candidates only when `LiftsRevision` or `RunsRevision` moved (event-driven, never per frame; with no candidate rule 7 is two field
writes).

## Bind notices

Log only, the UI shows nothing: a run that newly bound to a sign other than the selected one (`NoticeBinds`, on `RunsRevision`), and
the same for a lift that closed (`NoticeLiftCloses`, on `LiftsRevision`): its stats row became Running or Static (complete) and was
not complete under the same (entity, top) key at the previous rows, the census's "Closed" event (LiftCensus.cs header, Events) seen
through the stats rows, in a resort other than the one shown, the Not linked view included both ways. Without it, a gap redrawn from
the hub while it showed another ski area closed the lift in its own area silently. Both fire when the stats rebuild, i.e. while the
panel is open (or at its next open).

## Rename

`rename(e, text)`: e must be our live sign, run or station (a lift row whose id is a line edge has no station: refused with a line).
C# trims, cuts to 64 UTF-16 units (the vanilla name field's own maxLength: installed 1.6.2f1 index.js@1009013), never splitting a
surrogate pair, and calls `NameSystem.SetCustomName`, the call the vanilla title field makes, so both places show one name; empty
or whitespace removes the name (Game.UI/NameSystem.cs:214-241, same in 1.6.2: `m_Names` is written at once, CustomName +
BatchesUpdated through the EndFrameBarrier; saved, :549-575). A rename raises no tag and `NameSystem` has no rename counter (:162),
so the hub makes the next stats rebuild happen at once: `SkiResortStatsSystem.RequestRebuild("hub rename")`; for a run it also asks
the catalog to rebuild (`SkiRunCatalogSystem.RequestRebuild`, its names come from `NameSystem`). Next frame both run in UIUpdate
add order (catalog → stats), so the stats rows read the renamed catalog row. Two refinements, each logged in the rename line:

- A SIGN renamed to "" gets a fresh auto-name (`SkiRunBindTriggerSystem.PickSignName`: the smallest "Ski area N" no other live
  sign's custom name uses) written as its custom name instead of removing the name: removing it left the vanilla panel on the
  vanilla default while the hub showed a fallback label that could equal another sign's name (RENAME_TIP's "an empty name restores
  the default" still holds). Runs and stations: "" removes the name.
- A STATION shared by two or more lift rows is labeled "<name> → <top label>" and the rename field starts from that label; a
  trailing " → <top label>" of one of its rows is dropped before storing (`StripSharedBaseSuffix`), or an in-place edit would store
  the label and the suffix compounded. The UI still sends the edited text.

## Launching

The items are `SkiCatalog.BuildItems` (strip order: sign, the five lodge tiers, school, two patrol huts, each lift type's base /
top / line, the four runs); every per-item array is sized `SkiCatalog.BuildItems.Count` and `ItemRunKind` fixes each run item's
RunKind, never read from a prefab. Six optional numbers per item, read once at arm time (`ReadItemValues`; prefab data does not
change in a session): pricePerHa (`SkiRunFees.PerHaFee`), cost, costPerKm (`HubRules.LiftLinePerKm`), monthly / monthlyPerKm
(`SkiMoneyRules` lift rates) and monthlyPerHa (`SkiRunUpkeepSystem.CostPerHaMonth`); `price` is the run's base fee
(`SkiRunFees.BaseFee`), -1 for every other item. Values by kind: Line = the type row's `LinePerKm` / `FixedPerMonth` /
`PerKmPerMonth`; Station / Sign / Lodge / School / Patrol = `PlaceableObjectData.m_ConstructionCost`. A found prefab whose cost
differs from its row's informational mirror (`StationDef` / `LodgeDef` / `SchoolDef` / `PatrolDef` / `SignDef.BuildCost`) writes
one WARN per item per load ("Hub [items] drift: ... - the prefab wins").

- Hint: HINT[LiftNoPair] on a line item when `HubRules.LiftNoPair` holds over the shown area's rows of that lift type
  (`LiftStats.TypeIndex`; one scratch list reused per type, no per-recompute allocation), recomputed only when the stats
  `LiftsRevision` or the shown area changed.
- Sent: an item is offered this load when (its prefab name is Expected or its prefab is FOUND) and (it is not a Shuttle lift type's
  item, or `TramShuttleSystem` is registered: `World.GetExistingSystemManaged` at `ResolveItems` time, i.e. arm and catalog
  re-resolution). `build` writes the sent items only, table order. An Expected item that is MISSING is still sent, locked by
  REASON[Missing]; a pending item that is found is sent (Expected changes log severity only; the catalog writes the
  [pending-found] WARN). The launch refusal of a found unsent item says "moves as a Shuttle and TramShuttleSystem is not
  registered"; the DUMP's build line marks such an item "NOT SENT (no tram mover)".
- `ResolveItems` resolves each pair through `SkiPrefabCatalogSystem.TryGetPrefab` (per load, revalidated each frame);
  `RefreshLocks` re-resolves the items when the catalog's Generation moves (a "Hub [items]" WARN, at most
  `Tuning.CatalogItemLinesMax` per load); station tests ask the catalog (`IsStation`); the DUMP has a "PrefabCatalog: ..." line
  (`Describe()`) after the build line.
- Upgrade items: every item whose row `IsUpgrade` (lodge tiers, school, patrol huts) is a top-level building (no
  `ServiceUpgradeData`; tests/BuildingBatch P1) and is placed like a station after the hub's soft checks (below).
- Locks, precedence top to bottom, reason byte → locale key: 3 Missing (an Expected item's prefab MISSING), 1 Locked (progression),
  2 NoSign (an upgrade item, no live sign), 4 AlreadyBuilt (a ForbidMultiple row, today only the school, and a live building of
  that prefab already linked to the shown sign: `TopLevelConflict`). Reason 5 is retired (there is no limit on lodges of any
  tier), not reused. The Not linked view (no shown sign) applies only 3 / 1 / 2. The push signature multiplies the reason by 8.
  `TopLevelUpgradeLaunchAllowed` re-checks NoSign and AlreadyBuilt at launch time (the shown sign, else the one nearest the camera)
  and refuses with one Info line and `LastTriggerText`.
- Locked first: `ToolSystem.ActivatePrefabTool` itself does not check it (ToolSystem.cs:263-277, same in 1.6.2) while both vanilla
  callers do (ToolbarUISystem.cs:922-932, 1.6.2 :983; UpgradeMenuUISystem.cs:379-390):
  `EntityManager.HasEnabledComponent<Locked>(prefab entity)` (`Colossal.Entities.EntitiesExtensions`; Locked is
  `IEnableableComponent`, Game.Prefabs/Locked.cs). Our prefabs are off the toolbar (`UIObject.m_Group = null`):
  `ActivatePrefabTool` ignores groups (:263-278), `UIObject` still writes `UIObjectData` with a Null group (UIObject.cs:67-84) and
  the icon is the prefab's own `m_Icon`, so launching is unaffected.
- Sign, stations, lift, runs: `ActivatePrefabTool(prefab)`; the first tool whose `TrySetPrefab` takes it becomes active
  (ObjectToolSystem.cs:2987-3016, 1.6.2 :3046-3075: a non-upgrade ObjectPrefab → Create; AreaToolSystem.cs:3013-3021, 1.6.2 :3024;
  NetToolSystem.cs:5557-5569, 1.6.2 :5685). Another mod may insert a tool ahead of vanilla (ToolBaseSystem.cs:315), so the log
  names the tool that actually took it (toolID, ToolBaseSystem.cs:142) and its mode.
- "Active" (the item's tool is the active tool): `activeTool.GetPrefab()` is the item's prefab (`ObjectToolSystem.GetPrefab`
  returns it in Create/Brush/Stamp/Line/Curve, ObjectToolSystem.cs:2836-2847, 1.6.2 :2848-2859); the lift tool lights its type's
  line tile. Clicking an active item sets `activeTool = DefaultToolSystem` (the vanilla way, UpgradeMenuUISystem.cs:264-271).
- Upgrade-kind rows: `ActivatePrefabTool` like a station (no Upgrade mode, no 5 km circle), after the hub's soft checks
  (`TopLevelUpgradeLaunchAllowed`: a sign exists; the shown sign has no linked building of a ForbidMultiple row yet,
  `TopLevelConflict` over `SkiResortAttractivenessSystem.CopyTopLevelLinks`); the launch line names the sign nearest the camera and
  its distance. World selection and the Not linked view read `CopyTopLevelLinks` / `TryGetTopLevelLink`.

## Focus (fly, never select)

The `DebugSystemSearch.FocusEntity` recipe (Game.Debug/DebugSystemSearch.cs:625-645, 1.6.2 :646-666) minus its selection, on the
orbit camera (`CameraUpdateSystem.orbitCameraController` / `activeCameraController`; Game/OrbitCameraController.cs is identical in
1.6.2):

- `focusRun` / `focusBuilding` follow the entity: `followedEntity = e` (:47-71); the orbit camera tracks
  `SelectedInfoUISystem.TryGetPosition(e)` (:245-267, :286-300): an area at `Geometry.m_CenterPosition`
  (SelectedInfoUISystem.cs:623-628), a building at its Transform (:601-610). Zoom: runs length(Extents(`Geometry.m_Bounds.xz`)) x
  `FocusZoomFactor`; buildings the same over the bounds `TryGetPosition` returns (SelectedInfoUISystem.cs:578-651, public static),
  at least `FocusBuildingMinZoomM`.
- `focusLift(entity, top)`: a lift is a station pair, not one entity, so the camera flies to a point: `followedEntity = Null`,
  pivot = the point (:75-85); with no followed entity `UpdateCamera` re-clamps the pivot onto the terrain + 10 m every frame
  (:234-244, `kPivotVerticalOffset` :23). The point is the census row's `GapAt` when it has a gap (clicking a lift with a gap flies
  to the gap), else its `FocusCenter` with zoom `FocusRadius` x `FocusZoomFactor` (at least `FocusLiftMinZoomM`).
- `focusLift(node, Null)` from the LiftBend problem row (code 30): when no row matches (id, top) and top is Null, a row whose sharpest bent
  tower stands on that node (`LiftCensusSystem.TryGetRowOfBendNode`) flies to its `BendAt` at `FocusGapZoomM` (one `Hub [focus] lift bend` line).
- Zoom is clamped to the orbit's `m_ZoomRange` (:25); rotation x = `FocusPitchDeg` (DebugSystemSearch.cs:634); any camera input
  releases the camera back to the gameplay controller (`EventCameraMove` → `SelectedInfoUISystem.StopFollowing`,
  SelectedInfoUISystem.cs:311, :316-341).

### Yaw

The heading is kept from the camera the player is using, read from the ACTIVE controller before `TryMatchPosition`, not from the
orbit camera. The vanilla recipe writes `rotation = (45, orbit.rotation.y, 0)` after `TryMatchPosition` (DebugSystemSearch.cs:634,
1.6.2 :655), but `OrbitCameraController.rotation`'s getter returns its anchor's eulerAngles (OrbitCameraController.cs:114-119) while
`TryMatchPosition` writes only `m_Rotation` and the pivot (:206-221); the anchor turns only inside `UpdateCamera` (:256/:260/:270),
which runs only on the active controller (CameraUpdateSystem.cs:496, 1.6.2 :500). With the gameplay camera active the orbit's
getter returns a stale heading (the last orbit follow; identity on first use, inferred from `Awake` :154 creating the anchor, not
stated in the decompile) and the camera would swing to it. Every `IGameCameraController.rotation` is (pitch, yaw, 0) in degrees:
CameraController.cs:140-150, OrbitCameraController.cs:114-124, CinematicCameraController.cs:84-94 (all identical in 1.6.2).

### Switcher

‹ › flies the camera. The fly belongs to the request, never to the state: only `ApplySelectResort`, after an accepted switch whose
source is "ui" (the arrows are `selectResort`'s only sender, HubHeader.tsx `go()`; the dev tab has no SelectResort kind), calls
`FocusAfterSwitch`. `Select()` itself, world selection (rule 2), the event rules 1/4/5/6 (`ReconcileResorts`) and the binding
pushes never move the camera. `FocusAfterSwitch`:

- Not linked (Null): the camera stays: the view has no sign, its items can lie anywhere, and each of its rows flies on click.
- Already on the sign (`HubCameraRules.AlreadyOn`: the active controller's pivot within `KeepZoomFraction` x zoom of the sign's
  `TryGetPosition` point, and zoom <= `KeepMaxZoomM`) → the camera stays.
- Else → the focusBuilding path (`ApplyFocusBuilding`, the header pin's path: zoom max(radius x 2.5, `FocusBuildingMinZoomM`),
  heading kept, no selection).

One "Hub [focus]" line per switch: the target, the pivot's distance, the zoom and the verdict (fly / already on / Not linked: camera
not moved). A fly that fails writes `ApplyFocusBuilding`'s WARN instead. Rapid clicking flies once per accepted click (each is a
request; a glide takes <= 0.5 s, OrbitCameraController.cs:262-266). The trigger's name and argument are unchanged and the UI never
reads the camera.

## Auto-open

When `ToolSystem.selected` (ToolSystem.cs:101-111) changes to a live Ski Area Welcome Sign and the setting `OpenHubOnSignSelect` is
on, `hubOpen = true` once for that selection change; never forced closed. One field compare per frame.

## Handshake

The always-mounted `HubRoot` fires `uiHello(contract, bundleStamp)` once per UI load. One line per call: "UI hub connected:
contract N (DLL contract M), stamp ..."; "!!!" on a contract mismatch (the panel then shows the mismatch line, the Trees foldout
and the Build strip - building still works - and hides the statistics), and on a stamp mismatch unless the bundle stamp starts with
"ui-" (a UI-only npm build: an info line). The DLL's stamp is `SkiResortMod.BuildStamp.Value`, generated into obj/ by the csproj
target `SkiBuildStamp` ("no-ui" when built without the UI).

## Phase (Mod.cs)

UpdateAt UIUpdate, registered after `SkiRunCatalogSystem` and `SkiResortStatsSystem` (add order, UpdateSystem.cs:158-161): each
frame catalog → stats → hub, so the revisions this system reads are this frame's; every frame, paused included
(UIUpdateSystem.cs:17-20, SystemOrder.cs:67). gameMode => Game. `base.OnUpdate()` is called (UISystemBase.cs:39-46) although no
update binding is registered.

## Piste map

This system only exposes what `SkiPisteMapSystem` reads every frame: `PisteMapVisible` (armed, not disabled, the UI answered
uiHello with this DLL's contract (`HubContract`), the hub open and the toggle on), `ShownSign` (= the selected resort) and
`HoveredRun` (validated). The toggle is session-only (on at every load, `setPisteMap` / the dev tab's `TogglePisteMap`, one "Hub
[piste]" line each). `m_UiContractOk` is not reset by a load: the UI module outlives a city load.

## House contract

"created" line; one "Hub: ARMED" line per load listing every constant and the item costs; a one-shot "!!!" catch that closes the
hub, pushes contract = `ContractAbsent` (0) so the UI shows its "not connected" notice instead of launchers that silently do
nothing (`OnGamePreload` pushes `HubContract` back), and refuses every trigger (each refusal logged; a hoverRun only once per load,
then counted) until the next load. No per-frame managed allocation or string formatting (per frame: a few field reads, ValueBinding
updates on structs, bools and an Entity, revision compares, rule 7's pass flag, and a launch-state watch while a hub-launched lodge
is on the cursor; strings only on events).

Logs both ways: one line per trigger (hoverRun: counted), per hub open/close, per launch, per selected-resort change, per follow;
array pushes are counted (dev tab, DUMP) and logged only when an array's row count changed - a content refresh (snow, guests,
scores) can move a live revision twice a second while the panel is open, and a line per such push would be a flood. The ARMED line
names expected / pending / not-offered items; launches name the item id ("Hub [launch] lodgeMid -> ...", "!!! Hub upgrade launch
(school): ..."); the DUMP's build line marks not-sent items and its tool line reads "upgrade launch: ...". `BuildItemIds` (the dev
tab's launch buttons) lists all ids.

## DUMP

Dev tab / the hub's `dumpHub` trigger, event time only: this system's state, then the stats system's, the piste map's, the census,
the infoview, citizen presence, station power, `LodgeRoadSystem.AppendDump` (`AppendLodgeRoads`: resolved with
`GetExistingSystemManaged` at DUMP time like `m_PisteMap`, guarded so a throw lands in the DUMP text and never closes the hub), then
the catalog.

## Near HubContract

The contract number (bindings.ts `HUB_CONTRACT`): bump both together whenever a binding, trigger, field or meaning changes.
Appended fields and bindings by contract:

- 5: `CityStats.tripPlanner` and `tripPlannerBlocksSkiDays`.
- 6: the 25-row `SkiCatalog` build table, PROBLEM 23-25, LiftRow type / seats / speedMps, the ResortLive lodge-ladder fields.
- 7: ResortLive `passesSoldSeason`, `passIncomeSeason`, `passHoldersThisMonth`, `passHoldersLastMonth` and CityStats
  `bookTicketsAsFees` and `passShare`.
- 8: ResortLive `lessonsThisMonth`, `lessonsLastMonth`, `lessonIncomeMonth`, `passesPreSeason`, `passIncomePreSeason`,
  `cityIncomeEst`.
- 9: `CityStats.month` (the calendar month 0..11 the guest ledger counts).
- 10: the `history` binding (one `HistoryArea` row per live ski area, parallel monthly series oldest → newest with the month in
  progress last).
- 11: `netMonth` / `netEstimated` change meaning (all income booked this month incl. passes sold and lessons - all costs, the same
  as History's months) and ResortLive appends `passIncomeMonth`, `passIncomePreMonth`, `lodgeIncomeMonth`, `liftChargedMonth`,
  `runCostMonth`, `buildingsSoFar`, `wagesSoFar`, `monthShare`.
- 12: the `liftLine` value binding (`HubLiftLineState`) and the `liftLineToggle` trigger (`LiftLineToolSystem` on / off).
- 13: RunLive appends `snowDepthCm` and `gunWhy` (the run tooltip says why a run is open or closed and why its snow guns run or not).
- 14: the `grade` value binding (`HubLiftLineState` reused: available / active / state) and the `gradeToggle` trigger
  (`CatTrackGradeToolSystem` on / off).
- 15: HistoryArea appends `snowDepthCm` (the run snow depth at each month's roll, whole cm, -1 unknown; T142 B7b, history element v2).
- 16: the `grade` binding and the `gradeToggle` trigger are REMOVED (2026-10-24): the Cat track tile opens the cat track tool
  (`CatTrackGradeToolSystem`, which grades and builds the run in one action) through ApplyLaunch and lights while it is active
  (ActiveItemIndex), the lift-line tiles' pattern, with no area-tool fallback (every cat track is tool-built). RunLive.openState
  loses Unsuitable (3): a rated cat track is always Green (SkiRunRatingRules.CatTrackBand), so it reads Closed / Partly / Open.
- 17: the `quickRuns` value binding (`QuickRunsState`, with `QuickRunsCluster` rows) and the triggers `quickRunsToggle` and
  `quickRunsSet(key, value)` (the experimental Quick runs tool, QuickRunsToolSystem.md).
- 18: QuickRunsState appends `treesCost` (what Plant will cost: the ready tree preview's Temp m_Cost per planted tree times
  `plant`, 0 without a ready preview; the hub's tree counts line shows it, so a planting is never charged unannounced).
  Also in 18: the `deleteCatTrack` trigger (Entity; the cat track row's trash, DECISIONS 423b), queued and applied in OnUpdate through
  CatTrackGradeToolSystem.DeleteTrack.
- 19: the `catTrackDeleteMode` value (bool: the cat track tool is open AND in Delete mode; pushed in OnUpdate, so closing the tool or
  picking another shows it off) and the `setCatTrackDeleteMode(bool)` trigger (the Runs tab's Delete cat tracks chip: on opens the
  tool if needed, then its Delete mode; field test 2026-10-26b), queued and applied in OnUpdate.
- 20: QuickRunsState appends `rejects` (int[8]: the scan's reject histogram in the player's buckets, QuickRunsRules.RejectBuckets;
  zeros unless the scan has a result; the Quick runs part shows the top three as "N lines dropped: ...").
- 21: QuickRunsState appends `sitePercent` (int, the region's mean terrain grade in whole percent, -1 unknown) and `siteTier` (int, QuickRunsRules.SiteTier:
  0 unknown, 1 Gentle, 2 Steep, 3 Very steep, 4 Beyond; DECISIONS 443). The Resort Planner part draws one plain line (hubText QUICK_SITE_TEXT) under the
  region line and beside the mix line.
- 22: Resort Planner v2 (A7): QuickRunsState appends `pods` / `lifts` rows and `plan` (QuickRunsPlan); `quickRunsSet` gains `lifts`, `home`,
  `pod`, `lift`. Bumped on the integration branch at the A7 merge so its gates stay green; re-bump if main takes 22 first.
- 33: run types R3 (DECISIONS 578). `drawRun` {active, groomed, gunSpacingM, lightSpacingM}: the Run tile's option row, active while the
  Run tile's tool is open; trigger `setDrawRun`(key, value) with "groomed" (re-activates the area tool on the natural or groomed prefab),
  "guns", "lights"; SkiRunBindTriggerSystem seeds the sliders onto a run drawn groomed before its bind charge. The Run tile (`runGroomed`)
  draws every piste; `runNatural`, `runSnowmaking`, `runNight` are no longer sent. The Run tile's BuildItem carries its step prices
  (`naturalPrice`/`PerHa`, `guns*`, `lights*`). RunRow `paidSteps` (RUN_PAID bits). ResortLive's run costs by feature:
  `costGroomingMonth`, `costSnowmakingMonth`, `costLightsMonth`, `costCatTracksMonth` and `haNatural`, `haGroomed`, `haSnowmaking`,
  `haLit`, `haCatTracks` (replacing the per-kind groomed / snowmaking / night fields).
- 32: `runReplaced` {seq, from, to}: the last run re-emitted under a new entity (SkiRunReemitSystem, the Groomed toggle, DECISIONS 577); seq counts replacements this load, 0 = none. HubPanel moves a focused run row from `from` to `to`; C# moves the world selection (`ToolSystem.selected`, SelectedInfoUISystem.cs:456-462 is the same plain write), the hover request and the bind-notice sign map itself (`RunReplaced`). Trigger `setRunGroomed`(run, groomed) queues the re-emit; the UI control comes with run types R3.
- 31: LodgeParkingRow `name`: the lodge's custom name from the game's NameSystem (`TryGetCustomName`, NameSystem.cs:209-212), cleaned by `LodgeNumberRule.DisplayName` (one line, at most 64 UTF-16 units, the game's own rename field length; a cut name ends in an ellipsis), "" = none. The UI shows it instead of the tier word and number, as a plain string argument (check-bundle R19), in the `lodgeNameLabel` class the layout test allows to cut. A rename has no event: the game's rename (TitleSection.cs:48-52) only writes NameSystem's dictionary, so `PollLodgeNames` reads each row's entry every frame while the Guests view observes `lodgeParking` (one dictionary read per row; an unchanged name is the same string reference) and logs `Hub [lodge-name]` on a change.
- 30: LodgeParkingRow `number` (LodgeNumberSystem.ShownNumberOf: the lodge's persisted number among its area's lodges of its tier, 0 while it is the only one); the rows also rebuild when LodgeNumberSystem.Revision moves.
- 29: new binding `lodgeParking` (LodgeParkingRow: sign, lodge, item, carSpaces): per live sign, its active road lodges (`SkiLodgeParkingSystem.IsRoadLodge`) with their last census, -1 before the first; rebuilt when the snapshot generation or the census revision moves; a panel binding without Demand, read only by the Guests view (one row per lodge under the folds; a click flies to it).
- 28: RunRow `gunSpacingM`, `lightSpacingM`; triggers `setRunGunSpacing`, `setRunLightSpacing` (Entity, int): the Runs view's prop rows act on the focused run.
- 27: BusCardState `routeM` becomes `distanceM`, the straight line the bill counts: a coach's own PathInformation reads 0 once it drives, and a loaded coach never ran a probe.
- 26: new binding `busCard` (BusCardState), not a panel binding: the Ski Bus card beside the game's selected-info panel, shown while a coach or depot is selected. That selection also turns on Demand, for the ski area's name. The card is its own component at the "Game" hook, because a section inside the game's panel needs `moduleRegistry.extend` and an internal `game-ui/` path, which the gate bans.
- 25: ResortLive appends `busChargedMonth`, `depots`, `busGuestsMonth`, `busBillMonth`; PROBLEM 28-29 (`DepotNoHillLodge`, `DepotNoRoad`); build item `busDepot`, kind `depot`.
- 23: Resort Planner v2 (R3 fix round): QuickRunsState appends `canPlanAround` (bool, the census knows a lift station: gates the Region step's
  "click a lift station" hint); `plan.mountainMix` is [flat, green, blue, black + double black, 0] (QuickRunsPlanRules.MixPercents, same numbers as
  the tooltip); `plan.siteBenchHa` is a float (one decimal); a lift row's `on` means the lift AND its pod are on. Unshipped, like 22: renumber at the main merge.

## Safety net fence tile (no contract change)
The Runs tab's sixth tile, `safetyFence` (SkiCatalog item kind Fence, prefab SkiSafetyFence, PrefabID type NetFencePrefab, CatalogKind.Fence;
both enums append-only), launches through the generic path: ApplyLaunch -> ToolSystem.ActivatePrefabTool (the net tool, drawn like a path) and
ActiveItemIndex reads tool.GetPrefab(), like every non-lift tile. No cost or upkeep is sent (the prefab has no PlaceableObject cost): line 1 is the
name alone. Its tile icon is the prefab's own vanilla Media/Game/Icons/Fences path (tools/preflight.ps1 accepts Media/ as well as assetdb://).

## Quick runs (contract 17)
`SkiResortHubUISystem.QuickRuns.cs`. `quickRuns` is a `RawValueBinding` written by `WriteQuickRuns` (TypeBegin
"SkiResortMod.QuickRunsState", rule R2), a panel binding with no Demand: `PushPanelData` updates it only while observed, and only when
the tool's `HubRevision`, its availability or its activity moved (or on a resubscribe). Counts, totals and clusters are written as 0 /
empty unless the tool shows a preview. `quickRunsToggle` follows the glade toggle (Deactivate when active, else TryActivate, which
owns the gating and logs a refusal with its reason key); `quickRunsSet` calls `QuickRunsToolSystem.HubSet`, which ignores a call while
the tool is closed and clamps every value. Each call writes one `Hub [quickRuns]` log line; the DUMP gains one `quickRuns:` line.
`plant` is the whole slope's shortfall (TreeToPlant); buildDone / buildTotal and planting / treesDone / treesTotal carry the staged
build's and planting's progress (totals 0 when none is in flight; Undo trees reuses treesDone / treesTotal with planting false);
the `stop` key ends either one at its next boundary. `treesCost` (contract 18) is the planting cost the counts line shows before Plant.

### Resort Planner v2 fields (task A7; the contract number is bumped by the parent at the merge to main)
`WriteQuickRuns` appends `liftsKnob`, `home` and, while a Resort preview shows (`QuickRunsToolSystem.Plan` non-null), `pods`
(QuickRunsPod: id, key, letter, classKey, runs, enabledRuns, count[5], skiers, fee, on, liftId, homeKey), `planLifts` (QuickRunsLift: id,
key, origin, ordinal, typeKey, pod, lengthM, riseM, cost, serves, startsNearTop, homeKey, wantedKey, on) and `plan` (QuickRunsPlan: pods,
liftsProposed, liftsPinned, runKm, runKmPerKm2, deadEnds, mountainMix[5], sitePods, siteGreenHome, siteBenchHa); all empty / zero otherwise,
so the tool closed or a Region step writes the old fields plus those empty ones. They sit in the same function as the state's other
fields (rule R2 only follows one function per TypeBegin). `key` is the plan's uint FNV key written as a signed int (JSON numbers must
fit `quickRunsSet`'s int); class and verdict texts go as locale keys (QUICK_CLASS[*], QUICK_VERDICT[*]; the hub has them in SENT_KEYS), the
lift type as the catalog id for `LIFT_TYPE`. The pod rows come from `QuickRunsToolSystem.PodRows` (pure `QuickRunsPlanRules.PodRows`,
cached per `HubRevision`). New `quickRunsSet` keys: `lifts` (0 Auto, 1-9), `home` (0 Auto, 1 Green, 2 Blue), `pod` and `lift` (value =
the plan key; a toggle, refused while scanning or without a preview). The site verdict keeps `sitePercent` / `siteTier` (contract 21); the
detail numbers (sitePods, siteGreenHome, siteBenchHa) come from `ResortPlan.Site` (A6 fills them; zero pods = no detail line).
