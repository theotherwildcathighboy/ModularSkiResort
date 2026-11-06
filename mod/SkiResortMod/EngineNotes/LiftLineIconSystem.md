# LiftLineIconSystem and LiftPowerCueSystem: no vanilla electricity cue on a lift

Engine citations are `Game.X/File.cs:line` in tools/decompile/1.6.0f1.

## Rule (2026-11-02)

A lift shows no vanilla electricity icon except the base station's "low voltage not connected" icon while its feed is not wired.
The base's consumer warnings (no electricity, building bottleneck) go too: with no feed they repeat the low-voltage icon, and a city
short of power reads in the hub's lift status (LiftServedRule reads the flag, never the icon). The vanilla "high voltage not
connected" icon is replaced by our own "Lift line needed" icon (`SkiLiftLineNeeded`) on the same attach points. The placement
infoview is closed for every lift line and station tool (SkiPlacementInfoviewSystem.md). Pure rules: `LiftLineIconRule`
(tests/LiftBatch LI-1..LI-4).

Why: lifts are built on the power-line net class (the lift line is a `PowerLinePrefab`, a station has a `Transformer`), so vanilla
treats them as high-voltage lines and transformers and shows the player power-grid messages that make no sense for a ski lift.

## Inventory of the vanilla electricity cues on a lift

| Cue | Source | Status |
|---|---|---|
| Electricity infoview opened by the lift line tool | InfoviewInitializeSystem.cs:410-415 (`PowerLineData`) | removed: SkiPlacementInfoviewSystem closes it |
| Electricity infoview opened by every station tool | InfoviewInitializeSystem.cs:290 (`Transformer`) | removed: same system, every `SkiCatalog.Stations` row |
| "High voltage not connected" icon on a station (target: its stub node) and on a loose lift end | ConnectionWarningSystem.cs:2367, :2376-2401 (`UpdateConnectionWarnings`, `UpdateNodeConnectionWarnings` :1974-2010) | replaced: LiftLineIconSystem removes it in the same frame and shows `SkiLiftLineNeeded` |
| "Low voltage not connected" icon on the base station | ConnectionWarningSystem.cs:2366 | kept (StationLvIconSystem only clears it when the feed IS wired, a vanilla false positive) |
| Transformer bottleneck, not enough production, bottleneck icons on a station | ElectricityStatusSystem.cs:131-134 (`BuildingNotificationJob`) | removed: LiftPowerCueSystem |
| Bottleneck icon on a lift line edge | ElectricityStatusSystem.cs:66 (`NetEdgeNotificationJob`) | removed: LiftPowerCueSystem |
| No electricity, building bottleneck icons on a base or top station | DispatchElectricitySystem.cs:141-142 | removed: LiftPowerCueSystem (the flags stay; the hub reads them) |
| Transformer section in a station's info panel | TransformerSection.cs:82 (`Game.Buildings.Transformer`) | engine limit, accepted: hiding it needs a UI override of a vanilla section or removing `Transformer`, which the power path (PowerBridgeSystem) needs |
| Asset menu properties: transformer capacity / input / output on a station, power line capacity on a lift line | PrefabUISystem.cs:863-955, binders at :1598-1601 | engine limit, accepted: the binders read `TransformerData` and the net's voltage layers, both load-bearing for power |
| Electricity infoview drawing lift lines as high-voltage lines (when the player opens it) | the Electricity infomodes color power lanes by flow | engine limit, accepted; a mod-owned "Lifts" view is the way out (docs/research/lifts/lifts-infoview-feasibility-2026-11-02.md) |
| Electricity hover tooltip on a lift line or station (only inside that view, default tool) | RaycastElectricityTooltipSystem.cs:168, :172-247 | engine limit, accepted: it is a vanilla system for every power line |
| XP popup naming "Power line" for a drawn lift line | NetXPSystem.cs:76-110, :203; MilestoneUISystem.cs:359-368 | engine limit, accepted: the only lever is `PlaceableNetData.m_XPReward` = 0, which removes the lift's XP |
| Lift line upkeep billed to the Electricity service | CityServiceBudgetSystem.cs:566-582 (the line's `ServiceObjectData`, the vanilla HV line's Electricity service) | open, not changed: moving it is a money change to the line prefabs (`GetUpkeepCost` = round(length / 8) x the pieces' upkeep); not measured in game |

## LiftLineIconSystem: the same-frame override

ConnectionWarningSystem queues its icon commands in an `IconCommandBuffer`; IconCommandSystem plays every buffer back at
ModificationEnd (SystemOrder.cs:261, :271). Playback sorts the commands by owner, then null prefab first, then buffer index
(`Command.CompareTo`, IconCommandBuffer.cs:43-47), and skips a command when a LATER command for the same owner has the same prefab,
the same SecondaryLocation flag and the same target (IconCommandSystem.cs:106-160). Buffer indexes count up per
`CreateCommandBuffer` call in a frame (IconCommandSystem.cs:727-731). So a system that runs after ConnectionWarningSystem in the same
phase and creates its buffer then gets a higher index; its `Remove(station, hvIcon, stubNode)` makes vanilla's `Add` for that frame
a no-op. The vanilla icon never reaches the screen.

Registration (Mod.cs): `UpdateAfter<LiftLineIconSystem, ConnectionWarningSystem>(ModificationEnd)`. An anchored system is emitted
right after its anchor (UpdateSystem.cs:382-455), so it runs before IconCommandSystem (:271). A plain UpdateAt would sort after
IconCommandSystem and the icon would show for one frame each time.

When it runs: on the frames ConnectionWarningSystem runs (its own update query, Updated nodes / service upgrades / spawn and takeoff
locations or a RoadConnectionUpdated event, ConnectionWarningSystem.cs:2675-2693, plus `UpdateCollectSystem.mapTilesUpdated`,
:2721-2722), once after each Game load (a full pass: icons stored in a save), and every `PollFrames` (64) frames as a check that
removes a vanilla icon any pass missed. A tile purchase also gets the full pass over loose lift ends: vanilla then judges every node
inside the new tile, Updated or not (CollectOwnersJob2, :142, scheduled at :2747-2752), and the Updated-node query alone left those
ends showing the vanilla icon until the next poll (pre-release review 2026-11-06, finding 2). A new game starts with ConnectionWarningSystem's whole-map pass (:2710-2720), but a new city
has no lift.

What it judges:
- every station (a `SkiCatalog.Stations` prefab): its lift stub nodes are its sub-net nodes of the marker net or of any
  high-voltage connection net; `StationNeedsLine(stubs, lift edges at them not owned by the station)`;
- every lift node outside a station (Owner-less node of a lift line prefab): `LooseEndNeedsLine(lift edges)`: exactly one (a tower
  node has two).

Icon commands: `Add(owner, SkiLiftLineNeeded, IconPriority.Warning)` (the vanilla HV icon's priority, :2385-2393) while needed,
`Remove` when not; the HV removes for every sub-net node of a station and without a target.

## The icon prefab (LiftLineIconAsset, IconTextureCodec)

A `NotificationIconPrefab` made in code (`PrefabSystem.AddPrefab`), registered in `OnGamePreload`
so a saved icon finds its prefab when the save's prefab list is resolved. A new icon prefab entity is Created, which reruns
NotificationIconPrefabSystem (icon indexes, NotificationIconPrefabSystem.cs:50-95) and drops the render system's texture array
(`DisplayDataUpdated`, NotificationIconRenderSystem.cs:105-117), so a late registration is drawn.

Texture contract: NotificationIconRenderSystem builds ONE `Texture2DArray` sized to the largest icon, in the format of the last icon
it reads, and `Graphics.CopyTexture`s every icon into it (NotificationIconRenderSystem.cs:207-245). An icon of another size, mip
count or format would break that copy for every icon. So the vanilla icons' shape is read at runtime (all of them must agree, else
nothing is registered), and the PNG is resampled (alpha-weighted box), mip-chained and block-encoded to it (BC7 mode 6 or DXT5,
or raw RGBA32 / ARGB32). Any other format: not registered, "!!!" line, the vanilla icon stays (it is then the only cue).

Files: `Icons/SkiLiftLineNeeded.png` (512 px, rendered from the SVG) and `Icons/SkiLiftLineNeeded.svg` (the info panel's icon via
`UIObject.m_Icon` = `coui://ui-mods/SkiResortMod-icons/SkiLiftLineNeeded.svg`; `coui://ui-mods/` maps every UI module's folder,
ModManager.cs:468-479). Locale: `Notifications.TITLE[SkiLiftLineNeeded]` (TempNotificationTooltipSystem.cs:175 uses this scheme) and
`Notifications.DESCRIPTION[SkiLiftLineNeeded]`.

## LiftPowerCueSystem

DispatchElectricitySystem (offset 126) and ElectricityStatusSystem (offset 127) run once every 128 simulation frames in
GameSimulation (DispatchElectricitySystem.cs:222-230, ElectricityStatusSystem.cs:195-203, SystemOrder.cs:444, :448); the interval
gate is `updateIndex & (interval - 1) == offset` with `SimulationSystem.frameIndex` (UpdateSystem.cs:241, SimulationSystem.cs:279-286).
LiftPowerCueSystem is `UpdateAfter<_, ElectricityStatusSystem>(GameSimulation)`, runs every simulation frame and acts only when
`frameIndex & 127` is 126 or 127 (plus once after a load), queueing Removes in a buffer created after both vanilla buffers: the same
same-frame override. DispatchElectricitySystem writes only on a flag change (SetWarning, :144-157), so a Remove on its frames is
enough. Every station cue goes, so the net and building bottleneck prefabs may be one prefab without a special case.

## Logs and DUMP

LiftLineIconSystem: "created"; the prefab registration result once ("LiftIcon [prefab]: registered ..." with the vanilla texture
shape, or "!!!" with why not); ARMED per Game load (or "!!! ... NOT ARMED"); one `LiftIcon [show|clear]` line per owner whose state
changes, capped at `LogLinesMax` (64) per load; "!!!" once on a throw (then off until the next load). DUMP: passes, polls, HV removes
queued, HV icons a poll found (should stay 0: a non-zero count means a pass missed one), shown, cleared, owners judged, needing a line.
LiftPowerCueSystem: "created"; ARMED per Game load; "!!!" once on a throw. DUMP: passes, removes queued.
