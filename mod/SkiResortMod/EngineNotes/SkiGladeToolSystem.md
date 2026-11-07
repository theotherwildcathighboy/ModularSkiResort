# SkiGladeToolSystem: how the Glade tool hovers, previews, applies and checks itself

`SkiGladeToolSystem` is the Glade tool: a vanilla-style ToolBaseSystem. Hover a drawn ski run bound to a Ski Area Welcome Sign. The tool then previews "bring this run to tree density L" for one of two targets: the run interior, or its outside tree-line
belt. The preview uses ordinary Temp objects. They fell wild trees, remove this run's planted trees, or plant the missing trees of the
plan stream. A left click applies the plan through the vanilla pipeline (validation, cost tooltip, charging, refunds). Engine citations
are `Game.X/File.cs:line` in tools/decompile/1.6.0f1. "1.6.2 :N" notes a line shift in 1.6.2f1. The tool is re-derived from the vanilla
systems named. Nothing is copied from ParkManager (GPL).

## Settings

The tool has two settings:

- Level 1..8 (`SkiGladeCompose.Levels`, default DefaultLevel = 4). The persisted action is GladeNextVariant, Shift+G.
- Target interior / tree line (GladeNextMode, Alt+G).

The tool keeps both for the session. It does not store them per run.

`SkiGladeCompose` (pure) owns the count rule:

- Interior: T = round(area * InteriorDensity(L)). Compose removes own members first. Then it fells wild trees by hash rank, or plants
  T - N at the first unmatched slots.
- Tree line: BeltFactor(L) of the belt.

The tool hands it ComposeInput.Level / .TreeLine and the census. It emits what Compose returns. It tags new plantings variant 0
(NewPlantingVariant).

## Skeleton

base.OnCreate (ToolBaseSystem.cs:292-316) does three things. It registers the tool in ToolSystem.tools. It creates the Apply / Secondary
Apply / Cancel proxies. It sets Enabled=false. ToolSystem.ToolUpdate enables only the active tool (ToolSystem.cs:312-327). The
parameterless OnUpdate is sealed (ToolBaseSystem.cs:411-417), so the one-shot "!!!" catch wraps OnUpdate(JobHandle) (:420). TrySetPrefab
returns false for every prefab. Otherwise ToolSystem.ActivatePrefabTool would hand us asset-menu clicks (ToolSystem.cs:263-277).
Activation is TryActivate() -> ToolSystem.activeTool = this (ToolSystem.cs:84-98). These callers use it: SkiGladeHotkeySystem, the
"Ski Glades" dev tab and the hub (SkiResortHubUISystem: TryActivate("hub") / Deactivate, SetLevel / SetTreeLine, UnavailableReasonKey to
gray its buttons out).

## Bound runs

`SkiRunOwnershipSystem.IsBound(run)` = the run carries our SkiRunBinding and its sign is live. Runs carry no vanilla Owner(sign), because
it would let the bulldozer delete the whole resort. So m_RunQuery matches every surface area, and the run-prefab set narrows it to ours.
The tool still reads Owner on non-run entities. Owned trees are never thinnable. Planted / felled definitions carry a Null owner.

## Raycast

The masks are: typeMask Terrain|Areas, areaTypeMask Surfaces, and raycastFlags |= SubElements after base.InitializeRaycast().
InitializeRaycast clears flags and masks every frame (ToolBaseSystem.cs:430-443). These are the vanilla area tool's masks
(AreaToolSystem.cs:3051-3052, 1.6.2 :3062-3063). SubElements is inert for our un-owned runs. RaycastJobs.cs:95 applies it only when
typeMask2 != None, and only the Owner walk sets that (:84-88). The tool keeps SubElements so a run with a legacy Owner(sign) resolves to
the run. Never add StaticObjects (an owned hit would resolve to the building).

## Hover resolver, per frame

- (a) Input not live (controlOverWorld false, or raycast DebugDisable|UIDisable, ToolRaycastSystem.cs:105-112) -> HELD: hold the
  preview. ToolSystem.fullUpdateRequired (ToolSystem.cs:161, :258, :95) or raycastFlags & ToolDisable (ToolRaycastSystem.cs:98-101, a
  disabled input raycasts nothing, RaycastSystem.cs:772-775) -> SWITCHING. This state covers every hotkey activation's first frame(s).
  The tool holds the preview and never logs it.
- (b) No raycast result -> NOTHING.
- (c) The hit is one of our bound runs -> BOUND. The tool keeps the planned run when it also contains the point, because overlapping runs tie on
  distance (RaycastResult.cs:12-18).
- (d) Otherwise the tool scans our runs at the hit xz (bounds, then triangles with
  `MathUtils.Intersect(AreaUtils.GetTriangle3(nodes, tri).xz, p, out float2 _)`, RaycastJobs.cs:168). A bound run there -> BOUND
  "via point". The tool counts it. It logs once per run per activation, by what the raycast's winner was:
  - Another ground area (Area) won: an ordinary tie, INFO. Every ground-area result keeps HitEntity = terrain with m_NormalizedDistance
    reduced by 0.25/len (RaycastJobs.cs:299-304). So two ground areas at one point tie exactly, and RaycastResult.Accumulate keeps
    whichever came first (:14).
  - The terrain won, a raycast miss: WARN. The terrain result is t + 1/len (RaycastSystem.cs:177-181). So a validated ground-area result
    t + 0.75/len would have won. RaycastJobs.cs:46-120 accepts our un-owned Surface runs. So the area pass found nothing for a run that
    contains the point. This means one of two things: a search-tree / validation anomaly, or a raycast older than the geometry the scan
    reads (RaycastSystem.cs:806-835).

  One of our runs with Owner(sign) and no binding -> LEGACY (WARN once per run per load). Any other run of ours -> UNBOUND (no binding /
  paid orphan whose sign is gone / foreign Owner).
- (e) The hit has Area -> FOREIGN surface.
- (f) Otherwise -> TERRAIN.

## Logging

Silence must be impossible (`GladeHoverLog` in SkiGladeHover.cs). The log rules are:

- One line per hover-state change, deduplicated on (kind, run or hit entity, reason).
- At most `GladeHoverLog.MaxLinesPerActivation` (40) lines per activation. Then one "transitions capped" line. The tool writes it right
  after the Offer that reports CapJustReached, before the Legacy-WARN and Bound-with-re-plan early returns. CapJustReached is raised on
  one Offer per activation only.

The tool builds status strings for the dev tab on change only and keeps them in a two-slot memo. Stop logs the dwell summary before
ResetPreviewState. Every UpdateCore path feeds the dwell.

## Lifecycle: emit-and-hold (the vanilla idiom)

Vanilla's object tool emits nothing while the control point is unchanged (ObjectToolSystem.cs:4057-4058). It re-emits only on a change,
as Clear + UpdateDefinitions (:4100-4104). Holding works for three reasons:

- GenerateObjectsSystem consumes only definitions carrying Updated (GenerateObjectsSystem.cs:1730-1748).
- The game strips Updated at frame end (CleanUpSystem.cs:53).
- ValidationSystem only runs on Temp+Updated (ValidationSystem.cs:1917, 1930). So Error/Warning/Override persist while applyMode == None.
  ValidationSystem strips them when applyMode != None (:645). So never hold with Clear.

Destroying a definition never removes its Temp. Only the ClearTool / ApplyTool phases remove it (ToolOutputSystem.cs:22-30). The base
never enables the proxies for a mod tool (ToolBaseSystem.cs:362-364). WasPressedThisFrame is false unless enabled (UIInputAction.cs:62-69).
So every frame writes the enable flags. Cancel is enabled only while a preview is pending. This leaves Escape to the frontend's Back
(ToolUISystem.cs:280-304, 344-356).

- Re-plan (hover / target / level / forceUpdate / requested): the tool sets applyMode = Clear (ToolClearSystem.cs:88-106 deletes our old
  Temps). It calls DestroyDefinitions on the previous frames' definitions (GetDefinitionQuery excludes Updated, ToolBaseSystem.cs:689-692).
  It emits the new definitions with Updated.
- On the frame after an emission, the Temps exist and are validated ("ready"). On that frame, before the action flags, the tool runs the
  preview census, skip-set learning and the after-state hide. The tool ignores an Apply press on that same frame.
- After an Apply, the next ToolUpdate re-plans the same run. The applied trees are in the static tree by then. SearchSystem runs at
  Modification5 of the Apply frame. Deleted trees leave the tree there (SearchSystem.cs:91-99).

## Apply semantics

`PlanAndEmit` takes a census. Then it hands `SkiGladeCompose.Compose` these inputs:

- the run geometry, level and target
- the census minus this run's members (Existing/Thinnable)
- this run's members (Own)
- the known blocked slots (SkipSlots)
- the per-apply caps

It emits Compose's Fell and Remove lists as vanilla bulldoze-shaped delete definitions. It emits Compose's Plant list as planted-tree
definitions. Applying a level twice is a no-op. Large runs go in slices of <= 4,000 (provisional caps). A tree of this run that stands
outside its own layer's region after a run edit is MISPLACED. Compose removes such trees first. Apply takes the held preview only while it
is the plan of the current level and target (UpdateFrame "planCurrent").

- Skip set: on the ready frame, every canceled planted Temp maps back to its slot through m_EmittedSlots (the pipeline writes definition
  positions verbatim, GenerateObjectsSystem.cs:1253). The slot joins m_SkipSlots[PlanKey] (<= SkipKeysMax keys, oldest evicted).
  - Lifetime is one activation (cleared in OnStartRunning and at load). PlanKey hashes trees, vertex obstacles, exclusion rings and
    params but no road, building or map tile. So a longer-lived set kept a slot skipped after its blocker was removed.
  - The tool learns nothing when money may be short (PlayerMoney.money < PreviewPlant x MaxTreeCost). ValidationSystem also cancels
    Optional Temps for money (ValidationSystem.cs:1434-1449), and the tool cannot distinguish this per Temp. The state is MoneyShort.
  - A ready preview that learns new slots re-plans at once, and Apply waits for it. Under the count rule, a canceled spot leaves the run
    below T. Otherwise the re-plan after the Apply would plant replacements instead of reading "nothing to change".
- Plan cache: `SkiGladeCompose`'s GladePlanCache (PlanCacheEntries = 4, keyed by PlanKey). A saturated giant run burns
  MaxAttemptsPerLayer (200,000) per plan.

## Preview (after-state)

On the ready frame, every Delete Temp gets Game.Tools.Hidden + Game.Common.BatchesUpdated through the barrier's ECB. This is the idiom
vanilla uses on the originals (GenerateObjectsSystem.cs:976-977). Three facts support the hiding:

- The renderer reads the component (BatchDataSystem.cs:605).
- Apply reads only the Cancel/Delete flags (ApplyObjectsSystem.cs:337-352), and Delete touches only the original (:484-504).
- Old-Temp reuse is keyed on (prefab, sub-prefab, original, owner) (GenerateObjectsSystem.cs:56-79, 89-140). So a hidden delete Temp is
  only reused for a delete of the same original, and the reuse path never removes Hidden.

Hold frames fix Delete Temps whose Hidden state is wrong (self-heal, logged once per plan). Felled trees get a red ring. Removed members
get an orange ring. The rings are outline only, because a Projected fill would paint every mesh in its column. The tool does not use
Warning/Override recoloring. On Apply, ToolApplySystem deletes Warning entities and marks Override ones Overridden + Updated
(ToolApplySystem.cs:50-57, :118-126). Fallback if Hidden misbehaves in the field: the dev tab toggles hiding off, and the rings grow to
FallbackRingDiameter.

## Definitions

A planted tree is `CreationDefinition{prefab, no owner, stable m_RandomSeed, Optional}` + `ObjectDefinition{terrain-sampled y +
m_PlacementOffset.y, m_IsDecoration = true}` + Updated. A felled / removed tree is the vanilla bulldoze shape (BulldozeToolSystem.cs:664,
731-751, 812-875). The delete Temp hides each original. The collision iterator skips the hidden original (ValidationHelpers.cs:78). So an
apply's new trees never collide with the trees it removes. Both kinds of definition go through ToolOutputBarrier's ECB
(ObjectToolBaseSystem.cs:2446-2451).

## Census

The tool reads trees from SearchSystem's static tree on the main thread (GetStaticSearchTree(readOnly) + Complete,
SearchSystem.cs:421-425). It iterates per run triangle (inside) and per edge (band) with the AreaResourceSystem TreeIterator shape
(AreaResourceSystem.cs:621-660). A tree tagged SkiRunTreeMember with m_Run == this run goes to Own. Other runs' members go to Existing as
fixed occupants. The census counts Overridden trees too, because the iterator requires BoundsMask.IsTree only, not NotOverridden. It
counts them separately in m_CenOverriddenInside / Band.

### Why Overridden trees are counted

Trees are Overridable, not DeleteOverridden.

- Every ObjectGeometryPrefab that is not a pillar, utility or net object gets Overridable | Brushable
  (ObjectInitializeSystem.cs:578-605).
- Only buildings get DeleteOverridden (BuildingInitializeSystem.cs:859).

So OverrideSystem resolves two overlapping trees, or a tree under a net / building, by hiding one (Overridden, :1777-1791). It does not
delete the tree. An Overridden object loses BoundsMask.NotOverridden in the static tree (SearchSystem.cs:106, 138-140,
OverrideSystem.cs:74-80, 132-140, 158-161). This sequence follows when a felled tree is Deleted:

1. The apply deletes the felled tree (ApplyObjectsSystem.cs:484-504).
2. UpdateCollectSystem enqueues its bounds (:66-76).
3. SearchSystem removes the tree (:90-95).
4. OverrideSystem re-checks every object in those bounds (FindUpdatedObjectsJob :180-219 ignores the mask). It removes Overridden from
   a tree that no longer collides (:1777-1791). This restores NotOverridden.
5. A census that requires NotOverridden then sees the tree as a new untagged wild tree. A re-plan right after an apply
   reads "plant 5, fell 2".

The new untagged trees are not canceled Temps coming back. First, the game never creates a Temp with TempFlags.Cancel on the Apply frame
(ApplyObjectsSystem.cs:337-340, 474-482). Only ValidationSystem's ProcessValidationResultsJob.Cancel writes the flag (:1705-1712). Every
error on an Optional Temp ends there (:1667-1703). Second, SkiGladeTagSystem tags every Temp that the create path does create, in the
same ApplyTool phase (UpdateBefore ApplyObjectsSystem, SystemOrder.cs:714).

Visible consequences (accepted):

- "Trees now" includes hidden trees.
- A rank fell can take a hidden tree (red ring, no visible change) or uncover one (already counted).
- Trees hidden under a road crossing the run count as wild. The tool fells them at low levels.

The tag system mirrors ApplyObjectsSystem's decision order (Cancel -> Delete -> original with PrefabRef -> create). It does not count
delete Temps carrying Cancel as felled, because Cancel() keeps their original.

## Apply check

On the Apply frame, the tool snapshots three things:

- the census it planned from
- the planted spots that validation canceled
- the live fell / remove counts

The first re-plan after the apply re-takes the census of the applied run. It writes one "Glade: apply check" line. The line reports these counts: trees created vs tagged, own members, non-member trees, new untagged trees, vanished trees and un-overridden trees. It classifies new untagged trees as "at a canceled spot" / "near a felled tree" / "elsewhere". The line is INFO when every count balances, and WARN "APPLY MISMATCH"
otherwise. This costs one extra census per apply.

## Load reset

OnGameLoadingComplete resets the per-load flags. It also resets these items:

- ResetPreviewState
- the skip sets
- the plan cache (entries and hit / miss counters)
- the census counters printed by DUMP / the dev tab
- the legacy-warned set
- the candidate rings
- the plan counts the tooltip shows
- a pending apply check

## Scope and defensive contract

- The tool works in game mode only. It is inert (logged) when the run prefabs or the species pool are missing.
- It writes a "created" line.
- It writes an ARMED line per load that lists every constant.
- A one-shot "!!!" catch disables the tool until the next load.
- It never throws into the engine.
- It makes no per-frame managed allocation or string formatting.

## Near EmitPlant

- The planted tree has no owner. SubObjectSystem.RelocateSubObjects reconciles Owner(run) on any run Updated
  (SubObjectSystem.cs:1360-1533).
- Optional -> TempFlags.Optional (GenerateObjectsSystem.cs:1090-1093). A collision becomes a canceled Temp instead of an Apply-blocking
  Error (ValidationHelpers.cs:166-181, ValidationSystem.cs:1636, 1667-1712). See the vanilla tree brush (ObjectToolBaseSystem.cs:1024,
  1.6.2 :1027). Over-budget Optionals are canceled to fit (ValidationSystem.cs:1434-1449).
- m_IsDecoration = true leaves the archetype's Decoration enabled (GenerateObjectsSystem.cs:1311-1317, TreeObject.cs:25-29). The tree
  never ages (TreeGrowthSystem.cs:268-273) and counts no wood.
- The tool samples y here because the pipeline never terrain-snaps (GenerateObjectsSystem.cs:1253). The vanilla equivalent is
  ObjectToolBaseSystem.SampleTransform :1040-1072, 1.6.2 :1043.
- The tool records the slot by xz key. The Temp's Transform is the definition position verbatim (:1253, reuse path :1116). This is how a
  canceled Temp maps back to its slot (skip set).

## Quick runs package D (DECISIONS 415)
- The census treats Quick runs filler trees (`LayerFiller`) as wild. A run drawn later over the filler can thin or clear them like the
  map's own trees. Every other tagged tree stays Existing (never thinnable), as before.
- The glade tool implements `IGladeTagSource` (SkiGladeTagSystem.md) with its old Apply-frame guard.
- `EmitPlantDefinition`, `EmitThinDefinition` and `PositionKey` are `internal static`. So the tree filler emits exactly the glade tool's
  definitions and skip keys, with no copy.
