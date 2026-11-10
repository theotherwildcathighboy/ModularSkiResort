# SkiResortHubUISystem: bindings, triggers, selection rules, launch and camera behavior of the hub's C# side

The C# side of the Ski Resort hub: every binding and trigger of the "skiResort" group. The TS side is
mod/SkiResortUI/src/bindings.ts, the single source of truth for names and shapes. mod/SkiResortUI/tools/check-bundle.js matches
this file's binding/trigger literals and every `writer.TypeBegin("SkiResortMod.<X>")` block against it (rules R1, R2). The build
items are the `SkiCatalog.cs` BUILD-ITEMS table, which gate R16 reads. This file also covers these topics:

- the selected resort
- rename through the vanilla `NameSystem`
- the build launches
- the camera fly-to for runs, lifts and buildings
- the glade-tool controls
- auto-open on Welcome Sign selection
- the `uiHello` handshake
- what the piste-map overlay (`SkiPisteMapSystem`) reads

This system does not compute the per-area numbers. `SkiResortStatsSystem` owns them. This system only writes its rows (resorts /
resortLive / runs / runLive / lifts / problems / city) to the UI.

## Bindings

The value bindings are `hubOpen`, `contract`, `buildStamp`, `showHubButton`, `build`, `glade`, `resorts`, `resortLive`,
`selectedResort`, `runs`, `runLive`, `lifts`, `problems`, `city`, `pisteMap`, `follow` and `history`. The triggers are listed in
bindings.ts. The slow rows (resorts, runs, lifts) and the live rows (resortLive, runLive) are separate arrays. A snow, guest or
money change then re-sends only the small live arrays. `problems` is built in C# (`HubProblems.Build`). `pisteMap` (on at every
load) and `follow` (a counter, +1 per rule-7 switch) are ValueBindings that do not drive Demand. `hoverRun` is a field write, with
no queue, because mouse traffic must not fill `QueueCapacity`. The next OnUpdate validates it against the catalog and counts it. The
hub logs only its first valid call and first refused call per load.

## History binding

`history` is a `RawValueBinding` without Demand (only the History view subscribes it). It has one `HistoryArea` row per live ski
area, in the stats `resorts` order. Not linked is excluded: it has no sign and no history. Each row holds `sign`, `firstMonthKey`
(`SkiMoneyRules.MonthKey`: year x 12 + month 0..11) and 15 parallel arrays of the same length n. Index i = month firstMonthKey + i,
oldest → newest.

The hub builds the rows from the sign's `DynamicBuffer<SkiResortHistoryMonth>`, densified here. A Gap marker covers its key .. the
next element's key - 1. The hub sends every month of a Gap with flags GAP, flows 0, unknowns -1. The ledger's month in progress
comes last, with flags CURRENT (128, binding only, never stored) | PARTIAL when the ledger counts it since load. Months between the
last stored one and the month in progress are GAP.

The month in progress takes its values from these sources:

- Guests: the ledger's This (booked so far).
- `SkiDaySystem`'s `ArrivedThisMonth`.
- Its eight money terms = the ResortLive row's Net rows (`ResortLiveStats.NetTerms`). These are booked so far. Buildings / wages =
  the rate x the share of the month simulated. So its net is the Finances tile's `netMonth`.
- The ledger's This supplies the money terms only when that row has no terms yet (-1, the counter was not armed at the last rebuild).
- Attraction and snow %: the ResortLive point values now.

snowPct -1 = unknown (the stored 255).

The hub pushes `history` only while it is observed. It pushes only when one of these conditions holds:

- `SkiResortHistorySystem.Revision` moved (a stored month).
- The area set moved (stats `ResortsRevision`).
- `ProgressRevision` moved AND `HistoryProgressPushS` (5 s wall clock) passed since the last push.

The hub re-sends once on (re)subscribe. The payload is ~ 15 numbers x months x areas (5 years x 5 areas ~ 4,500 numbers).

## Binding mechanics

The system registers all bindings with `AddBinding`, never `AddUpdateBinding`. `AddUpdateBinding` would re-send a raw binding every
frame (UISystemBase.cs:48-58). These `Colossal.UI.Binding` facts come from the decompile of the installed 1.6.2f1 DLL.
`IJsonWriter` has Write(bool/int/uint/long/ulong/float/double/string). The constructor is
`ValueBinding<T>(group, name, initial, IWriter<T> writer = null, EqualityComparer<T> comparer = null)`.

- `ValueBinding<T>.Update` pushes only when its comparer reports a change (ValueBinding.cs:37-44) and only while JS observes it
  (`TriggerUpdate` checks `active`, :46-53). A subscribe pushes the current value at once (`OnSubscribe`, :24-35).
  hubOpen/contract/buildStamp/showHubButton are bool/int/string (ValueWriters.cs:14-36). `glade` is a struct implementing
  `IJsonWritable` + `IEquatable` (scalar fields, no boxing). `selectedResort` is `ValueBinding<Entity>` (precedent
  UpgradeMenuUISystem.cs:130, SelectedInfoUISystem.cs:146). `city` is `ValueBinding<CityStats>` with our writer and comparer
  (`CityStatsWriter` / `CityStatsComparer`). So the dedup and the TypeBegin name do not depend on how `SkiResortStatsSystem`
  declares the struct.
- `RawValueBinding` re-sends the whole payload on every `Update()` while observed, with no dedup (RawValueBinding.cs:47-56), and on
  subscribe (:22-33). The hub pushes the six arrays resorts / resortLive / runs / runLive / lifts / problems only when their own
  revision of `SkiResortStatsSystem` moved (content-hash gated by the stats system). It re-sends them once on (re)subscribe. It
  pushes `build` when a signature of every field it sends changed. A push whose row count changed writes `Hub [push] <name> rows=...`
  through that array's `LogWindow`(10 s). `problems` follows simulation state, e.g. a factor flipping around 100 % in a brownout.
  The system flushes folds on the next `PushArray`, in `OnGamePreload` and in `OnDestroy`.
- `observerCount` / `active` are public (EventBindingBase.cs:14-16). Only panel-mounted components subscribe every binding except
  hubOpen / contract / showHubButton / buildStamp (bindings.ts header). So "observed" = "the panel is open". This system sets
  `SkiRunCatalogSystem.Demand` and `SkiResortStatsSystem.Demand` every frame to "any of build, resorts, resortLive, runs, runLive,
  lifts, problems, city observed" (pisteMap and follow do not demand). So nothing is computed for the panel while it is closed (the
  catalog's rating service runs anyway).
- Rows are typed objects written token by token (TypeBegin / PropertyName / Write, JsonWriter.cs: one native call per token, no JSON
  string). Every TypeBegin name is "SkiResortMod.<TS interface>" (the UI ignores `__Type`). The system writes an Entity as {index,
  version} (UnityWriters.cs:17-25), and `TriggerBinding<Entity>` reads that shape back (UnityReaders.cs:19-27). Numbers go through
  `Fin()`. `Fin()` sends a non-finite float or double as 0 and counts it (one WARN per load), because one NaN must not break the
  UI's JSON.
- **One shape per TypeBegin name (engine fact, in game, R3 DECISIONS 578).** Objects written under the same TypeBegin name must carry
  the same properties in the same order. R3 first wrote the Run tile's eight step fields (`naturalPrice` ... `lightsMonthlyPerHa`) on
  the Run BuildItem only. The game process exited the frame the hub opened. It left no log line, no crash report and no WER entry.
  The bisect ran over 8 launches: drawRun alone was fine, and the conditional fields alone killed it. Fix: every BuildItem writes every field,
  with -1 where it does not apply. The UI tests `>= 0`. Never put a PropertyName inside a condition in a TypeBegin writer.
- The system writes every number of the stats structs through the `Fin` overloads (int / float / double). It casts enum-like fields
  (kind, state, band, season) to int. So the C# file compiles whatever numeric type the stats structs pick.
- The binding catches a trigger exception and logs it to the vanilla UI log, not ours (TriggerBinding.cs:21-31, :58-69, :87-99). So
  every trigger body here has its own try/catch and "!!!" line.

## Triggers

Every trigger body follows these rules:

- It has a try/catch.
- It checks Enabled. A `UISystemBase` is disabled outside its gameMode at `OnGamePreload` (UISystemBase.cs:60-64), but its bindings
  stay attached until `OnDestroy` (:29-37). So triggers still arrive.
- It checks that any entity argument exists. A Null argument is allowed only for `selectResort` and the `top` of `focusLift`.
- It QUEUES the request. OnUpdate (main thread, UIUpdate) applies the request and writes one log line per trigger.

Exceptions to the one log line per trigger: `uiError` logs once per distinct message. `uiLayout` logs only when the string changed.
C# never gates a trigger on the UI's contract: a bundle of another contract still launches tools.

## Selected resort

`m_SelectedResort` is not saved. It holds a live Ski Area Welcome Sign, or `Entity.Null` = the "Not linked" view. These rules apply,
each with one "Hub [select]" line:

1. Load: Null. At the first stats rebuild of the load with >= 1 sign: the first sign in stable order.
2. World selection change (one field compare per frame). The mapping runs BEFORE the auto-open early return. The mapping is:
   - Our sign → it.
   - An entity whose Owner is our sign → the owner. Example: the lodge, because vanilla ServiceUpgrade buildings carry Owner = the
     sign.
   - Our station → the resort of the first census row touching it (resolved from that row's base station, else its top station). A
     station on no row → the resort of its own position (`SkiResortStatsSystem.TryResolveResort`: one rule).
   - Anything else → unchanged, no line.

   The line names what was selected.

   Known limit: clicking the sign that is already selected in the world does not switch the hub back to it after the player used
   ‹ ›. `DefaultToolSystem.SelectTempEntity` writes `ToolSystem.selected` only when the hit entity or index differs
   (Game.Tools/DefaultToolSystem.cs:1176-1189, same in 1.6.2). toggleSelected comes from the cancel action (:903). So the per-frame
   compare sees no change. To detect the re-click, the hub would need the tool's Apply action. `ToolBaseSystem.applyAction` is
   protected (ToolBaseSystem.cs:188) and `InputManager.toolActionCollection` is internal (InputManager.cs:536). Neither is reachable
   without reflection. The "Tool"/"Apply" ProxyAction names are asset data, unverified. Workaround: click elsewhere first, or use ‹ ›.
3. `selectResort(e)`: a live sign, or Null (refused while the Not linked row is empty). An accepted switch from the UI also moves
   the camera (see Switcher below).
4. A sign that was not in the previous resort rows → selected ("new sign": covers signs placed from the hub).
5. The selected sign is no longer live → the first remaining sign, else Null.
6. Null selected and the Not linked row is empty → the first sign (stays Null with no sign).
7. The hub follows what you build. The trigger events, in a live ski area other than the shown one, are:
   - a run that newly bound
   - a station newly placed
   - a lift that newly closed

   The hub then switches to that area, through `HubRules.FollowCheck`'s guards:
   - only while the hub is open
   - never in a pass where rule 1/4/5/6 already switched
   - not within `FollowQuietAfterUserS` (2 s) of the player's own switch (rules 2 and 3 stamp it)
   - at most one follow per `FollowMinIntervalS` (2 s)

   Each pass has one candidate, the most deliberate act first (a lift closed, then a station placed, then a run bound). The follow
   writes one "Hub [follow]" line instead of the "Hub [select]" line. It bumps the `follow` binding (the UI's "Now showing ..."
   notice). It never moves the camera and never ends a lodge on the cursor. A suppressed follow writes nothing (the bind / lift
   notice of the pass names the event). The hub counts a suppressed follow per reason (DUMP `hub v3:`). The hub sees builds made
   while it was closed at its next open (one follow).

Switching resort in the hub never selects anything in the world. Rules 1, 4, 5, 6 run only when `ResortsRevision` moved. Rule 7's
candidates run only when `LiftsRevision` or `RunsRevision` moved (event-driven, never per frame). With no candidate, rule 7 is two
field writes.

## Bind notices

Bind notices are log only, and the UI shows nothing. Two notices exist:

- `NoticeBinds` (on `RunsRevision`): a run that newly bound to a sign other than the selected one.
- `NoticeLiftCloses` (on `LiftsRevision`): the same for a lift that closed. Its stats row became Running or Static (complete) and
  was not complete under the same (entity, top) key at the previous rows. This is the census's "Closed" event (LiftCensus.cs header,
  Events) seen through the stats rows. It applies in a resort other than the one shown, the Not linked view included both ways.

Without these notices, a gap redrawn from the hub while it showed another ski area closed the lift in its own area silently. Both
fire when the stats rebuild, i.e. while the panel is open (or at its next open).

## Rename

`rename(e, text)`: e must be our live sign, run or station. A lift row whose id is a line edge has no station, so the trigger refuses
it with a line. C# trims the text and cuts it to 64 UTF-16 units (the vanilla name field's own maxLength: installed 1.6.2f1
index.js@1009013). It never splits a surrogate pair. It then calls `NameSystem.SetCustomName`, the call the vanilla title field
makes, so both places show one name. An empty or whitespace text removes the name. See Game.UI/NameSystem.cs:214-241 (same in
1.6.2): `m_Names` is written at once, and CustomName + BatchesUpdated go through the EndFrameBarrier. The name is saved (:549-575).
A rename raises no tag, and `NameSystem` has no rename counter (:162). So the hub makes the next stats rebuild happen at once:
`SkiResortStatsSystem.RequestRebuild("hub rename")`. For a run, it also asks the catalog to rebuild
(`SkiRunCatalogSystem.RequestRebuild`, because its names come from `NameSystem`). Next frame both run in UIUpdate add order
(catalog → stats), so the stats rows read the renamed catalog row. Two refinements apply. Each is logged in the rename line:

- A SIGN renamed to "" gets a fresh auto-name (`SkiRunBindTriggerSystem.PickSignName`: the smallest "Ski area N" no other live
  sign's custom name uses). The hub writes it as the custom name instead of removing the name. Removing the name left the vanilla
  panel on the vanilla default. At the same time, the hub showed a fallback label that could equal another sign's name (RENAME_TIP's
  "an empty name restores the default" still holds). For runs and stations, "" removes the name.
- A STATION shared by two or more lift rows is labeled "<name> → <top label>", and the rename field starts from that label. Before
  storing, the hub drops a trailing " → <top label>" of one of its rows (`StripSharedBaseSuffix`). Otherwise an in-place edit would
  store the label and the suffix compounded. The UI still sends the edited text.

## Launching

The items are `SkiCatalog.BuildItems` (strip order: sign, the five lodge tiers, school, two patrol huts, each lift type's base /
top / line, the four runs). Every per-item array is sized `SkiCatalog.BuildItems.Count`. `ItemRunKind` fixes each run item's
RunKind, never read from a prefab. Each item has six optional numbers. The hub reads them once at arm time (`ReadItemValues`).
Prefab data does not change in a session. The six numbers are:

- pricePerHa (`SkiRunFees.PerHaFee`)
- cost
- costPerKm (`HubRules.LiftLinePerKm`)
- monthly (`SkiMoneyRules` lift rates)
- monthlyPerKm (`SkiMoneyRules` lift rates)
- monthlyPerHa (`SkiRunUpkeepSystem.CostPerHaMonth`)

`price` is the run's base fee (`SkiRunFees.BaseFee`), -1 for every other item. Values by kind: Line = the type row's `LinePerKm` /
`FixedPerMonth` / `PerKmPerMonth`. Station / Sign / Lodge / School / Patrol = `PlaceableObjectData.m_ConstructionCost`. A found
prefab whose cost differs from its row's informational mirror (`StationDef` / `LodgeDef` / `SchoolDef` / `PatrolDef` /
`SignDef.BuildCost`) writes one WARN per item per load ("Hub [items] drift: ... - the prefab wins").

- Hint: a line item carries HINT[LiftNoPair] when `HubRules.LiftNoPair` holds over the shown area's rows of that lift type
  (`LiftStats.TypeIndex`). The hub reuses one scratch list per type, so a recompute allocates nothing. It recomputes only when the
  stats `LiftsRevision` or the shown area changed.
- Sent: the hub offers an item this load when both of these are true:
  - Its prefab name is Expected or its prefab is FOUND.
  - It is not a Shuttle lift type's item, or `TramShuttleSystem` is registered (`World.GetExistingSystemManaged` at `ResolveItems`
    time, i.e. arm and catalog re-resolution).

  `build` writes the sent items only, in table order. An Expected item that is MISSING is still sent, locked by REASON[Missing]. A
  pending item that is found is sent. Expected changes log severity only. The catalog writes the [pending-found] WARN. The launch
  refusal of a found unsent item says "moves as a Shuttle and TramShuttleSystem is not registered". The DUMP's build line marks such
  an item "NOT SENT (no tram mover)".
- `ResolveItems` resolves each pair through `SkiPrefabCatalogSystem.TryGetPrefab` (per load, revalidated each frame).
  `RefreshLocks` re-resolves the items when the catalog's Generation moves (a "Hub [items]" WARN, at most
  `Tuning.CatalogItemLinesMax` per load). Station tests ask the catalog (`IsStation`). The DUMP has a "PrefabCatalog: ..." line
  (`Describe()`) after the build line.
- Upgrade items: every item whose row `IsUpgrade` (lodge tiers, school, patrol huts) is a top-level building (no
  `ServiceUpgradeData`, see tests/BuildingBatch P1). The hub places it like a station after the hub's soft checks (below).
- Locks, precedence top to bottom, reason byte → locale key:
  - 3 Missing (an Expected item's prefab MISSING)
  - 1 Locked (progression)
  - 2 NoSign (an upgrade item, no live sign)
  - 4 AlreadyBuilt (a ForbidMultiple row, today only the school, and a live building of that prefab already linked to the shown
    sign: `TopLevelConflict`)

  Reason 5 is retired (there is no limit on lodges of any tier). The hub does not reuse it. The Not linked view (no shown sign)
  applies only 3 / 1 / 2. The push signature multiplies the reason by 8. `TopLevelUpgradeLaunchAllowed` re-checks NoSign and
  AlreadyBuilt at launch time (the shown sign, else the one nearest the camera). It refuses with one Info line and `LastTriggerText`.
- Locked first: `ToolSystem.ActivatePrefabTool` itself does not check it (ToolSystem.cs:263-277, same in 1.6.2), but both vanilla
  callers do (ToolbarUISystem.cs:922-932, 1.6.2 :983, and UpgradeMenuUISystem.cs:379-390). The check is
  `EntityManager.HasEnabledComponent<Locked>(prefab entity)` (`Colossal.Entities.EntitiesExtensions`; Locked is
  `IEnableableComponent`, Game.Prefabs/Locked.cs). Our prefabs are off the toolbar (`UIObject.m_Group = null`).
  `ActivatePrefabTool` ignores groups (:263-278). `UIObject` still writes `UIObjectData` with a Null group (UIObject.cs:67-84), and
  the icon is the prefab's own `m_Icon`. So launching is unaffected.
- Sign, stations, lift, runs: the hub calls `ActivatePrefabTool(prefab)`. The first tool whose `TrySetPrefab` takes the prefab
  becomes active:
  - ObjectToolSystem.cs:2987-3016, 1.6.2 :3046-3075: a non-upgrade ObjectPrefab → Create
  - AreaToolSystem.cs:3013-3021, 1.6.2 :3024
  - NetToolSystem.cs:5557-5569, 1.6.2 :5685

  Another mod may insert a tool ahead of vanilla (ToolBaseSystem.cs:315). So the log names the tool that actually took the prefab
  (toolID, ToolBaseSystem.cs:142) and its mode.
- "Active" (the item's tool is the active tool): `activeTool.GetPrefab()` is the item's prefab (`ObjectToolSystem.GetPrefab`
  returns it in Create/Brush/Stamp/Line/Curve, ObjectToolSystem.cs:2836-2847, 1.6.2 :2848-2859). The lift tool lights its type's
  line tile. Clicking an active item sets `activeTool = DefaultToolSystem` (the vanilla way, UpgradeMenuUISystem.cs:264-271).
- Upgrade-kind rows: the hub calls `ActivatePrefabTool` like for a station (no Upgrade mode, no 5 km circle), after the hub's soft
  checks. `TopLevelUpgradeLaunchAllowed` makes these checks: a sign exists, and the shown sign has no linked building of a
  ForbidMultiple row yet (`TopLevelConflict` over `SkiResortAttractivenessSystem.CopyTopLevelLinks`). The launch line names the sign
  nearest the camera and its distance. World selection and the Not linked view read `CopyTopLevelLinks` / `TryGetTopLevelLink`.

## Focus (fly, never select)

This is the `DebugSystemSearch.FocusEntity` recipe (Game.Debug/DebugSystemSearch.cs:625-645, 1.6.2 :646-666) minus its selection. It
runs on the orbit camera (`CameraUpdateSystem.orbitCameraController` / `activeCameraController`). Game/OrbitCameraController.cs is
identical in 1.6.2.

- `focusRun` / `focusBuilding` follow the entity: `followedEntity = e` (:47-71). The orbit camera tracks
  `SelectedInfoUISystem.TryGetPosition(e)` (:245-267, :286-300): an area at `Geometry.m_CenterPosition`
  (SelectedInfoUISystem.cs:623-628), a building at its Transform (:601-610). Zoom for runs: length(Extents(`Geometry.m_Bounds.xz`))
  x `FocusZoomFactor`. Zoom for buildings: the same over the bounds `TryGetPosition` returns (SelectedInfoUISystem.cs:578-651,
  public static), at least `FocusBuildingMinZoomM`.
- `focusLift(entity, top)`: a lift is a station pair, not one entity, so the camera flies to a point: `followedEntity = Null`,
  pivot = the point (:75-85). With no followed entity, `UpdateCamera` re-clamps the pivot onto the terrain + 10 m every frame
  (:234-244, `kPivotVerticalOffset` :23). The point is the census row's `GapAt` when it has a gap (clicking a lift with a gap flies
  to the gap). Otherwise it is the row's `FocusCenter` with zoom `FocusRadius` x `FocusZoomFactor` (at least `FocusLiftMinZoomM`).
- `focusLift(node, Null)` comes from the LiftBend problem row (code 30). If no row matches (id, top) and top is Null, the hub looks
  for a row whose sharpest bent tower stands on that node (`LiftCensusSystem.TryGetRowOfBendNode`). The camera flies to that row's
  `BendAt` at `FocusGapZoomM` (one `Hub [focus] lift bend` line).
- The orbit's `m_ZoomRange` clamps the zoom (:25). Rotation x = `FocusPitchDeg` (DebugSystemSearch.cs:634). Any camera input
  releases the camera back to the gameplay controller (`EventCameraMove` → `SelectedInfoUISystem.StopFollowing`,
  SelectedInfoUISystem.cs:311, :316-341).

### Yaw

The hub keeps the heading from the camera the player is using. It reads the heading from the ACTIVE controller before
`TryMatchPosition`, not from the orbit camera. The vanilla recipe writes `rotation = (45, orbit.rotation.y, 0)` after
`TryMatchPosition` (DebugSystemSearch.cs:634, 1.6.2 :655). But `OrbitCameraController.rotation`'s getter returns its anchor's
eulerAngles (OrbitCameraController.cs:114-119), while `TryMatchPosition` writes only `m_Rotation` and the pivot (:206-221). The
anchor turns only inside `UpdateCamera` (:256/:260/:270). `UpdateCamera` runs only on the active controller (CameraUpdateSystem.cs:496,
1.6.2 :500). With the gameplay camera active, the orbit's getter returns a stale heading, and the camera would swing to it. The stale
heading is the last orbit follow, or identity on first use. The identity case is inferred from `Awake` :154 creating the anchor. The
decompile does not state it. Every `IGameCameraController.rotation` is (pitch, yaw, 0) in degrees: CameraController.cs:140-150,
OrbitCameraController.cs:114-124, CinematicCameraController.cs:84-94 (all identical in 1.6.2).

### Switcher

‹ › flies the camera. The fly belongs to the request, never to the state. Only `ApplySelectResort` calls `FocusAfterSwitch`, after an
accepted switch whose source is "ui". The arrows are `selectResort`'s only sender (HubHeader.tsx `go()`), and the dev tab has no
SelectResort kind. `Select()` itself, world selection (rule 2), the event rules 1/4/5/6 (`ReconcileResorts`) and the binding pushes
never move the camera. `FocusAfterSwitch` works like this:

- Not linked (Null): the camera stays. The view has no sign, its items can lie anywhere, and each of its rows flies on click.
- Already on the sign (`HubCameraRules.AlreadyOn`: the active controller's pivot within `KeepZoomFraction` x zoom of the sign's
  `TryGetPosition` point, and zoom <= `KeepMaxZoomM`) → the camera stays.
- Else → the focusBuilding path (`ApplyFocusBuilding`, the header pin's path: zoom max(radius x 2.5, `FocusBuildingMinZoomM`),
  heading kept, no selection).

The hub writes one "Hub [focus]" line per switch. The line holds the target, the pivot's distance, the zoom and the verdict (fly /
already on / Not linked: camera not moved). A fly that fails writes `ApplyFocusBuilding`'s WARN instead. Rapid clicking flies once per accepted click,
because each click is a request. A glide takes <= 0.5 s (OrbitCameraController.cs:262-266). The trigger's name and argument are
unchanged, and the UI never reads the camera.

## Auto-open

When `ToolSystem.selected` (ToolSystem.cs:101-111) changes to a live Ski Area Welcome Sign and the setting `OpenHubOnSignSelect` is
on, the hub sets `hubOpen = true` once for that selection change. The hub never forces itself closed. One field compare per frame.

## Handshake

The always-mounted `HubRoot` fires `uiHello(contract, bundleStamp)` once per UI load. The system writes one line per call: "UI hub
connected: contract N (DLL contract M), stamp ...". It writes "!!!" in two cases:

- On a contract mismatch. The panel then shows the mismatch line, the Trees foldout and the Build strip (building still works), and
  hides the statistics.
- On a stamp mismatch, unless the bundle stamp starts with "ui-" (a UI-only npm build: an info line).

The DLL's stamp is `SkiResortMod.BuildStamp.Value`, generated into obj/ by the csproj target `SkiBuildStamp` ("no-ui" when built
without the UI).

## Phase (Mod.cs)

UpdateAt UIUpdate, registered after `SkiRunCatalogSystem` and `SkiResortStatsSystem` (add order, UpdateSystem.cs:158-161). Each
frame runs catalog → stats → hub, so the revisions this system reads are this frame's. It runs every frame, paused included
(UIUpdateSystem.cs:17-20, SystemOrder.cs:67). gameMode => Game. The system calls `base.OnUpdate()` (UISystemBase.cs:39-46), although
it registers no update binding.

## Piste map

This system only exposes what `SkiPisteMapSystem` reads every frame:

- `PisteMapVisible`: true when all of these hold:
  - armed
  - not disabled
  - the UI answered uiHello with this DLL's contract (`HubContract`)
  - the hub is open
  - the toggle is on
- `ShownSign` (= the selected resort)
- `HoveredRun` (validated)

The toggle is session-only (on at every load, `setPisteMap` / the dev tab's `TogglePisteMap`, one "Hub [piste]" line each).
`m_UiContractOk` is not reset by a load: the UI module outlives a city load.

## House contract

The house contract has these parts:

- A "created" line.
- One "Hub: ARMED" line per load, listing every constant and the item costs.
- A one-shot "!!!" catch. It closes the hub and pushes contract = `ContractAbsent` (0), so the UI shows its "not connected" notice
  instead of launchers that silently do nothing (`OnGamePreload` pushes `HubContract` back). It refuses every trigger until the next
  load. The system logs each refusal, but a hoverRun only once per load, then counts it.

There is no per-frame managed allocation or string formatting. Per frame, the system does only these things:
- a few field reads
- ValueBinding updates on structs, bools and an Entity
- revision compares
- rule 7's pass flag
- a launch-state watch while a hub-launched lodge is on the cursor

The system builds strings only on events.

The system logs one line for each of these events:
- a trigger (hoverRun is counted instead)
- a hub open/close
- a launch
- a selected-resort change
- a follow

The dev tab and the DUMP count array pushes. The system logs a push only when an array's row count changed. A content refresh (snow,
guests, scores) can move a live revision twice a second while the panel is open. A line per such push would be a flood. The ARMED
line names expected / pending / not-offered items. Launches name the item id ("Hub [launch] lodgeMid -> ...", "!!! Hub upgrade launch
(school): ..."). The DUMP's build line marks not-sent items, and its tool line reads "upgrade launch: ...". `BuildItemIds` (the dev
tab's launch buttons) lists all ids.

## DUMP

The DUMP comes from the dev tab or the hub's `dumpHub` trigger, at event time only. It lists these parts in order:

1. This system's state.
2. The stats system's state.
3. The piste map's state.
4. The census.
5. The infoview.
6. Citizen presence.
7. Station power.
8. `LodgeRoadSystem.AppendDump` (`AppendLodgeRoads`). The DUMP resolves it with `GetExistingSystemManaged` at DUMP time, like
   `m_PisteMap`. A guard makes a throw land in the DUMP text and never close the hub.
9. The catalog.

## Near HubContract

Bump the contract number here and in bindings.ts (`HUB_CONTRACT`) together whenever a binding, trigger, field or meaning changes.
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
- 11: `netMonth` / `netEstimated` change meaning: all income booked this month incl. passes sold and lessons - all costs, the same
  as History's months. ResortLive appends `passIncomeMonth`, `passIncomePreMonth`, `lodgeIncomeMonth`, `liftChargedMonth`,
  `runCostMonth`, `buildingsSoFar`, `wagesSoFar`, `monthShare`.
- 12: the `liftLine` value binding (`HubLiftLineState`) and the `liftLineToggle` trigger (`LiftLineToolSystem` on / off).
- 13: RunLive appends `snowDepthCm` and `gunWhy` (the run tooltip says why a run is open or closed and why its snow guns run or not).
- 14: the `grade` value binding (`HubLiftLineState` reused: available / active / state) and the `gradeToggle` trigger
  (`CatTrackGradeToolSystem` on / off).
- 15: HistoryArea appends `snowDepthCm` (the run snow depth at each month's roll, whole cm, -1 unknown, T142 B7b, history element v2).
- 16: the `grade` binding and the `gradeToggle` trigger are REMOVED (2026-10-24). The Cat track tile opens the cat track tool
  (`CatTrackGradeToolSystem`, which grades and builds the run in one action) through ApplyLaunch. The tile lights while the tool is
  active (ActiveItemIndex), the same pattern as the lift-line tiles. There is no area-tool fallback, because every cat track is
  tool-built. RunLive.openState loses Unsuitable (3). A rated cat track is always Green (SkiRunRatingRules.CatTrackBand), so it
  reads Closed / Partly / Open.
- 17: the `quickRuns` value binding (`QuickRunsState`, with `QuickRunsCluster` rows) and the triggers `quickRunsToggle` and
  `quickRunsSet(key, value)` (the experimental Quick runs tool, QuickRunsToolSystem.md).
- 18: QuickRunsState appends `treesCost` (what Plant will cost: the ready tree preview's Temp m_Cost per planted tree times
  `plant`, 0 without a ready preview). The hub's tree counts line shows it, so a planting is never charged unannounced. Also in 18:
  the `deleteCatTrack` trigger (Entity, the cat track row's trash, DECISIONS 423b). The hub queues it and applies it in OnUpdate
  through CatTrackGradeToolSystem.DeleteTrack.
- 19: the `catTrackDeleteMode` value and the `setCatTrackDeleteMode(bool)` trigger.
  - `catTrackDeleteMode` is a bool: the cat track tool is open AND in Delete mode. The hub pushes it in OnUpdate, so closing the
    tool or picking another shows it off.
  - `setCatTrackDeleteMode(bool)` is the Runs tab's Delete cat tracks chip: on opens the tool if needed, then its Delete mode (field
    test 2026-10-26b). The hub queues the trigger and applies it in OnUpdate.
- 20: QuickRunsState appends `rejects` (int[8]: the scan's reject histogram in the player's buckets, QuickRunsRules.RejectBuckets).
  The values are zeros unless the scan has a result. The Quick runs part shows the top three as "N lines dropped: ...".
- 21: QuickRunsState appends `sitePercent` (int, the region's mean terrain grade in whole percent, -1 unknown). It also appends
  `siteTier` (int, QuickRunsRules.SiteTier: 0 unknown, 1 Gentle, 2 Steep, 3 Very steep, 4 Beyond). See DECISIONS 443. The Resort Planner part draws
  one plain line (hubText QUICK_SITE_TEXT) under the region line and beside the mix line.
- 22: Resort Planner v2 (A7): QuickRunsState appends `pods` / `lifts` rows and `plan` (QuickRunsPlan). `quickRunsSet` gains `lifts`,
  `home`, `pod`, `lift`. The integration branch carries this bump from the A7 merge, so its gates stay green. Re-bump if main takes
  22 first.
- 42: the runs-first plans (Resort Planner round, task R2-b). `QuickRunsState` loses `mix`, `liftsKnob` and `home`. `QuickRunsLift` loses
  `wantedKey` and gains `flagged`, `waits`, `overLimitTowers`. `QuickRunsPlan` loses `pods` and gains `trackKm`. `QuickRunsState` gains `tracks`
  and `QuickRunsPod` gains `tracks`. `runs`, `enabled`, `count` and `variety` count pistes only. `QuickRunsSheetRow` gains `flagged`, `waits`,
  `overLimitTowers`, `overLimitTower`, `overLimitAlongM`, `overLimitBendDeg` and `overLimitCapDeg`. See "Runs-first plans (contract 42)" below.
- 41: the plan sheet's lifts (Resort Planner round, task R1-d). `QuickRunsState` appends `sheet` (`QuickRunsSheet`: rows, running,
  index, total, stopping, toBuild, unbuilt, built, cost, notice, with `QuickRunsSheetRow` rows). The trigger `quickLiftsAct(action, key)`
  is new (QuickLiftsAction: buildAll, build, stop, clear). See "Plan sheet lifts (contract 41)" below.
- 40: the receipt's missing lines (icon scheme). `ResortLive` appends `varietyBands` (int: bits 1 green, 2 blue, 4 black or harder of
  `SkiResortScore.Result.VarietyBands`, the counted Rated runs) and `verticalShortM` (float: `HubRules.VerticalShortM`, meters to the full
  vertical bonus). Both are -1 without a score (no Result, Not linked). The Variety row's "Missing:" line and flag name the cleared bits.
  The UI never recomputes the variety rule (D10).
- 39: merge warning (SkiMergeSystem.md, Hub section). `MergeCardState` and `MergeOverlayState` append `geometry` and `reachM`.
  `geometry` is true when some ski area has a run outline or a lift station (`TryGetSlopesideBounds`). `reachM` is `SlopesideMaxM` in
  meters, for the warning text. With no geometry nothing is Near. The card shows the warning and turns Merge off. The Buildings tab
  chip shows the warning instead of 0 / 0.
- 37: merge discoverability (SkiMergeSystem.md, Hub section): `MergeCardState` appends `near` (bool: within SlopesideMaxM of any
  area's run outline or lift station, `NearAnySlopeside`). So the card says before a merge whether the building would count. The
  Buildings tab's Mergeable buildings toggle (BuildStrip, under the building tiles) fires the same `armMergeOverlay` and reads
  `mergeOverlay` through OkFooter.
- 36: merge overlay (SkiMergeSystem.md, Overlay section): the `mergeOverlay` value binding (`MergeOverlayState` {armed, allowed,
  tooSmall}, the Merged buildings fold's chip and its tooltip counts) and the trigger `armMergeOverlay(on)`.
- 35: merge asset (SkiMergeSystem.md, Hub section). It adds these items:
  - the `mergeCard` value binding (`MergeCardState`, the card beside the game's selected-info panel)
  - the `mergedLodges` binding (`MergedLodgeRow`, the Overview's Merged buildings fold)
  - the triggers `mergeBuilding(building, tier)` and `unmergeBuilding(building)`

  This entry was contract 32 on the merge-asset branch. It was renumbered when main reached 34.
- 34: `CityStats.bookTicketsAsFees` removed (always fees, the UI never read it, DECISIONS 607).
- 33: run types R3 (DECISIONS 578). `drawRun` {active, groomed, gunSpacingM, lightSpacingM}: the Run tile's option row, active while
  the Run tile's tool is open. The trigger `setDrawRun`(key, value) has the keys "groomed" (re-activates the area tool on the natural
  or groomed prefab), "guns" and "lights". SkiRunBindTriggerSystem seeds the sliders onto a run drawn groomed before its bind
  charge. The Run tile (`runGroomed`) draws every piste. `runNatural`, `runSnowmaking`, `runNight` are no longer sent. The Run
  tile's BuildItem carries its step prices (`naturalPrice`/`PerHa`, `guns*`, `lights*`). RunRow `paidSteps` (RUN_PAID bits).
  ResortLive's run costs by feature: `costGroomingMonth`, `costSnowmakingMonth`, `costLightsMonth`, `costCatTracksMonth` and
  `haNatural`, `haGroomed`, `haSnowmaking`, `haLit`, `haCatTracks` (replacing the per-kind groomed / snowmaking / night fields).
- 32: `runReplaced` {seq, from, to}: the last run re-emitted under a new entity (SkiRunReemitSystem, the Groomed toggle,
  DECISIONS 577). seq counts replacements this load, 0 = none. HubPanel moves a focused run row from `from` to `to`. C# moves the world
  selection (`ToolSystem.selected`, SelectedInfoUISystem.cs:456-462 is the same plain write), the hover request and the bind-notice
  sign map itself (`RunReplaced`). Trigger `setRunGroomed`(run, groomed) queues the re-emit. The UI control comes with run types R3.
- 31: LodgeParkingRow `name`: the lodge's custom name from the game's NameSystem (`TryGetCustomName`, NameSystem.cs:209-212).
  `LodgeNumberRule.DisplayName` cleans it: one line, at most 64 UTF-16 units (the game's own rename field length), and a cut name
  ends in an ellipsis. "" = none. The UI shows it instead of the tier word and number. It passes it as a plain string argument
  (check-bundle R19), in the `lodgeNameLabel` class that the layout test allows to cut. A rename has no event: the game's rename
  (TitleSection.cs:48-52) only writes NameSystem's dictionary. So `PollLodgeNames` reads each row's entry every frame while the Guests
  view observes `lodgeParking`. That is one dictionary read per row, and an unchanged name is the same string reference. It logs
  `Hub [lodge-name]` on a change.
- 30: LodgeParkingRow `number` (LodgeNumberSystem.ShownNumberOf: the lodge's persisted number among its area's lodges of its tier,
  0 while it is the only one). The rows also rebuild when LodgeNumberSystem.Revision moves.
- 29: new binding `lodgeParking` (LodgeParkingRow: sign, lodge, item, carSpaces): per live sign, its active road lodges
  (`SkiLodgeParkingSystem.IsRoadLodge`) with their last census, -1 before the first. The hub rebuilds it when the snapshot
  generation or the census revision moves. It is a panel binding without Demand, read only by the Guests view (one row per lodge
  under the folds, a click flies to it).
- 28: RunRow `gunSpacingM`, `lightSpacingM`. Triggers `setRunGunSpacing`, `setRunLightSpacing` (Entity, int). The Runs view's prop
  rows act on the focused run.
- 27: BusCardState `routeM` becomes `distanceM`, the straight line the bill counts. A coach's own PathInformation
  reads 0 once it drives, and a loaded coach never ran a probe.
- 26: new binding `busCard` (BusCardState), not a panel binding: the Ski Bus card beside the game's selected-info panel, shown while
  a coach or depot is selected. That selection also turns on Demand, for the ski area's name. The card is its own component at the
  "Game" hook. The reason: a section inside the game's panel needs `moduleRegistry.extend` and an internal `game-ui/` path, which the
  gate bans.
- 25: ResortLive appends `busChargedMonth`, `depots`, `busGuestsMonth`, `busBillMonth`. PROBLEM 28-29 (`DepotNoHillLodge`,
  `DepotNoRoad`). Build item `busDepot`, kind `depot`.
- 23: Resort Planner v2 (R3 fix round): QuickRunsState appends `canPlanAround` (bool, the census knows a lift station: gates the
  Region step's "click a lift station" hint). `plan.mountainMix` is [flat, green, blue, black + double black, 0]
  (QuickRunsPlanRules.MixPercents, same numbers as the tooltip). `plan.siteBenchHa` is a float (one decimal). A lift row's `on` means
  the lift AND its pod are on. Unshipped, like 22: renumber at the main merge.

## Plan sheet lifts (contract 41)
The Resort Planner's plan sheet (ResortPlanSheet, StationPlaceToolSystem.md) reaches the hub in two parts.

**The `sheet` field.** `WriteQuickRuns` writes it at the end of the state, inside the same function (rule R2). It does not depend on the
preview. The rows come from `ResortLiftBuildSystem.Sheet`, so they stay after the run build and with the planner closed.
- `rows` (QuickRunsSheetRow) have these fields:
  - key (the row key as a signed int), name ("P1"), typeKey (catalog id).
  - lengthM (horizontal chord of the two spots), riseM, cost (the estimate, clamped to int).
  - on, state (SheetLiftState name), step, reasonKey.
  - `step` is "building" for the lift in flight, "queued" for a key waiting in the queue, else "".
  - `reasonKey` is "" or `SkiResort.Tooltip.QUICK_LIFT_WHY[group]`. The group comes from `ResortPlanSheet.ReasonGroup`. A waiting row carries its
    own group (PLAN_TRAIN ..., BUILD_REFUSED, WAIT: `ResortPlanSheet.WaitingKey`), never GAME.
  - `total` is the run's total and a Stop does not shrink it (R1-f). `index` is `ResortPlanSheet.QueueIndexOf`.
- `running`, `index`, `total`, `stopping`: the queue. `index` and `total` give "Building lift N / M" and are 0 when idle. `stopping`
  is true after Stop while a lift is still in flight.
- `toBuild` (on and not built), `unbuilt`, `built`, `cost` (the estimate of the `toBuild` lifts): the lifts total.
- `notice`: a locale key for the latest refused lift call, "" when none. It ends after 420 frames.

**The push.** `PushQuickRuns` also compares the sheet's `Revision`, the queue's `QueueRevision` and whether a notice stands. Any change
pushes the binding. The queue revision moves with every start, step, end, stop and notice, because none of those change a sheet row.

**The trigger `quickLiftsAct(action, key)`.** The hub queues it like every trigger. `ApplyQuickLiftsAct` calls
`ResortLiftBuildSystem.HubAct`, which calls the queue's entry points (`BuildAll`, `BuildOne`, `Stop`, `ClearSheet`). They only queue keys
or empty the sheet, so they are safe in the hub's update (no command buffer). A call the queue cannot take gets a notice and a log line
`ResortLifts [hub]`, and the hub adds `Hub [quickLifts]`:
- Not armed or disabled: notice Off.
- `plannerBusy` (the planner has a phase other than Region, or plants trees): notice RunsBusy. Reason: a lift build takes the tool from the
  planner and drops the preview. Applies to buildAll, build and clear.
- buildAll with no lift on and unbuilt: Nothing. buildAll or build when every such lift is queued: Queued. build of an unknown or built row: Nothing.
- clear while the queue runs: Busy.
- stop is never refused (idle is a log line).

The DUMP gains a `planSheet:` line (rows, revision, queue state, lifts to build and their estimate).

## Safety net fence tile (no contract change)
The Runs tab's sixth tile is `safetyFence` (SkiCatalog item kind Fence, prefab SkiSafetyFence, PrefabID type NetFencePrefab,
CatalogKind.Fence). Both enums are append-only. It launches through the generic path: ApplyLaunch -> ToolSystem.ActivatePrefabTool
(the net tool, drawn like a path). ActiveItemIndex reads tool.GetPrefab(), like every non-lift tile. The hub sends no cost or upkeep
(the prefab has no PlaceableObject cost): line 1 is the name alone. Its tile icon is the prefab's own vanilla
Media/Game/Icons/Fences path (tools/preflight.ps1 accepts Media/ as well as assetdb://).

## Quick runs (contract 17)
`SkiResortHubUISystem.QuickRuns.cs`. `quickRuns` is a `RawValueBinding` written by `WriteQuickRuns` (TypeBegin
"SkiResortMod.QuickRunsState", rule R2). It is a panel binding with no Demand. `PushPanelData` updates it only while observed, and
only when the tool's `HubRevision`, its availability or its activity moved (or on a resubscribe). The hub writes counts, totals and
clusters as 0 / empty unless the tool shows a preview. `quickRunsToggle` follows the glade toggle (Deactivate when active, else
TryActivate, which owns the gating and logs a refusal with its reason key). `quickRunsSet` calls `QuickRunsToolSystem.HubSet`, which
ignores a call while the tool is closed and clamps every value. Each call writes one `Hub [quickRuns]` log line. The DUMP gains one
`quickRuns:` line. `plant` is the whole slope's shortfall (TreeToPlant). buildDone / buildTotal and planting / treesDone /
treesTotal carry the staged build's and planting's progress (totals 0 when none is in flight; Undo trees reuses treesDone /
treesTotal with planting false). The `stop` key ends either one at its next boundary. `treesCost` (contract 18) is the planting cost
the counts line shows before Plant.

### Resort Planner v2 fields (task A7; the contract number is bumped by the parent at the merge to main)
Contract 42 removed `liftsKnob` and `home`. While a Resort preview shows (`QuickRunsToolSystem.Plan` non-null), it also appends:
- `pods` (QuickRunsPod: id, key, letter, classKey, runs, enabledRuns, tracks, count[5], skiers, fee, on, liftId, homeKey)
- `planLifts` (QuickRunsLift: id, key, origin, ordinal, typeKey, pod, lengthM, riseM, cost, serves, startsNearTop, homeKey, flagged, waits,
  overLimitTowers, on)
- `plan` (QuickRunsPlan: liftsProposed, liftsPinned, runKm, trackKm, runKmPerKm2, deadEnds, mountainMix[5], sitePods, siteGreenHome,
  siteBenchHa)

All are empty / zero otherwise, so the tool closed or a Region step writes the old fields plus those empty ones. They
sit in the same function as the state's other fields (rule R2 only follows one function per TypeBegin). `key` is the plan's uint
FNV key written as a signed int (JSON numbers must fit `quickRunsSet`'s int). Class and verdict texts go as locale keys
(QUICK_CLASS[*], QUICK_VERDICT[*]). The hub has them in SENT_KEYS. The lift type goes as the catalog id for `LIFT_TYPE`. The pod
rows come from `QuickRunsToolSystem.PodRows` (pure `QuickRunsPlanRules.PodRows`, cached per `HubRevision`). The new `quickRunsSet`
keys are (the tool still accepts `mix`, `lifts`, `home` and `shuffle` and ignores them since R2-a, the hub does not send them):

- `pod` (value = the plan key; a toggle, refused while scanning or without a preview)
- `lift` (value = the plan key; a toggle, refused while scanning or without a preview)

The site verdict keeps `sitePercent` / `siteTier` (contract 21). The detail numbers (sitePods, siteGreenHome, siteBenchHa) come from
`ResortPlan.Site` (A6 fills them; zero pods = no detail line).

## Runs-first plans (contract 42)
Task R2-b changed what `WriteQuickRuns` writes for the runs-first planner. Every row object still writes every field in every branch.

- `QuickRunsShown.Of(tool)` (same file) counts again. `QuickTotals` counts a cat track as a green run, because a cat track rates Green by rule.
  `QuickRunsShown` splits the published runs into pistes and cat tracks (`RunFeatures.FromKind(kind).IsCatTrack`), in total and per group, and
  sums the cat track km. The tooltip system uses the same class (`QuickRunsTooltipSystem.Shown`, cached per hub revision and result).
- State: `runs`, `enabled`, `count[5]` and `variety` are pistes only. `tracks` is the enabled cat tracks. `fee` and `skiers` still come from the totals and
  include the tracks. `target` is all zeros with runs-first. The hub then prints the counts without a target.
- Pod rows: `runs` and `enabledRuns` are pistes. `count` is enabled pistes by band. `tracks` is all cat tracks of the group.
- Lift rows: `flagged` is `PlanLift.BuildsFlagged`. `waits` is `HandFit` without `BuildsFlagged`. `overLimitTowers` is `PlanLift.OverLimitTowers`.
- Sheet rows: `flagged` and `waits` come from the plan of the same key (`t.Result.Resort`, `FindPlanLift`), because the sheet does not hold them.
  They are false once the tool drops the plan. The over-limit fields come from `ResortPlanSheet.Row`. They are the count and the tower counted
  from the base (-1 with none). They also give the distance along the line, the bend and the limit of its sheave train. The row state stays Built.
- The hub reads `PLAN_TRAINS` through the existing `ReasonGroup` path. The lift engine already groups it. Only the locale key was missing.

## Unbound pistes (contract 43)
`unbound` is `QuickRunsToolSystem.UnboundPistes` while a preview shows, else 0. It is the N of QUICK_WARN[NoSignSome] ("N of M runs",
M = `enabled`). The warnings array holds keys only, so the number needs its own field.
