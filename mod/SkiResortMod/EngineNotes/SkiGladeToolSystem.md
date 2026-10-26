# SkiGladeToolSystem: how the Glade tool hovers, previews, applies and checks itself

`SkiGladeToolSystem` is the Glade tool: a vanilla-style ToolBaseSystem. Hover a drawn ski run bound to a Ski Area Welcome Sign and
it previews "bring this run to tree density L" for one of two targets (run interior, or its outside tree-line belt) as ordinary
Temp objects: felling wild trees, removing this run's planted trees, or planting the missing trees of the plan stream. Left click
applies through the vanilla pipeline (validation, cost tooltip, charging, refunds). Engine citations are `Game.X/File.cs:line` in
tools/decompile/1.6.0f1; "1.6.2 :N" notes a line shift in 1.6.2f1. The tool is re-derived from the vanilla systems named; nothing
is copied from ParkManager (GPL).

## Settings

Level 1..8 (`SkiGladeCompose.Levels`, default DefaultLevel = 4; persisted action GladeNextVariant, Shift+G) and target interior /
tree line (GladeNextMode, Alt+G), kept for the session, not stored per run. `SkiGladeCompose` (pure) owns the count rule
(interior: T = round(area * InteriorDensity(L)); remove own members first, then fell wild trees by hash rank, or plant T - N at the
first unmatched slots; tree line: BeltFactor(L) of the belt). The tool hands it ComposeInput.Level / .TreeLine and the census and
emits what it returns; new plantings are tagged variant 0 (NewPlantingVariant).

## Skeleton

base.OnCreate (ToolBaseSystem.cs:292-316) registers the tool in ToolSystem.tools, creates the Apply / Secondary Apply / Cancel
proxies and sets Enabled=false; ToolSystem.ToolUpdate enables only the active tool (ToolSystem.cs:312-327). The parameterless
OnUpdate is sealed (ToolBaseSystem.cs:411-417), so the one-shot "!!!" catch wraps OnUpdate(JobHandle) (:420). TrySetPrefab returns
false for every prefab, otherwise ToolSystem.ActivatePrefabTool would hand us asset-menu clicks (ToolSystem.cs:263-277).
Activation is TryActivate() -> ToolSystem.activeTool = this (ToolSystem.cs:84-98), called by SkiGladeHotkeySystem, the "Ski
Glades" dev tab and the hub (SkiResortHubUISystem: TryActivate("hub") / Deactivate, SetLevel / SetTreeLine, UnavailableReasonKey to
gray its buttons out).

## Bound runs

`SkiRunOwnershipSystem.IsBound(run)` = the run carries our SkiRunBinding and its sign is live. Runs carry no vanilla Owner(sign) (it
would let the bulldozer delete the whole resort), so m_RunQuery matches every surface area and the run-prefab set narrows it to
ours. Owner is still read on non-run entities (owned trees are never thinnable; planted / felled definitions carry a Null owner).

## Raycast

typeMask Terrain|Areas, areaTypeMask Surfaces, raycastFlags |= SubElements after base.InitializeRaycast() (which clears flags and
masks every frame, ToolBaseSystem.cs:430-443), the vanilla area tool's masks (AreaToolSystem.cs:3051-3052; 1.6.2 :3062-3063).
SubElements is inert for our un-owned runs (RaycastJobs.cs:95 applies it only when typeMask2 != None, set only inside the Owner
walk :84-88) but kept so a run with a legacy Owner(sign) resolves to the run. Never add StaticObjects (an owned hit would resolve
to the building).

## Hover resolver, per frame

(a) Input not live (controlOverWorld false, or raycast DebugDisable|UIDisable, ToolRaycastSystem.cs:105-112) -> HELD: hold the
    preview. ToolSystem.fullUpdateRequired (ToolSystem.cs:161, :258, :95) or raycastFlags & ToolDisable (ToolRaycastSystem.cs:
    98-101; a disabled input raycasts nothing, RaycastSystem.cs:772-775) -> SWITCHING: every hotkey activation's first frame(s);
    held, never logged.
(b) No raycast result -> NOTHING.
(c) The hit is one of our bound runs -> BOUND (keep the planned run when it also contains the point: overlapping runs tie on
    distance, RaycastResult.cs:12-18).
(d) Otherwise a point scan of our runs at the hit xz (bounds, then triangles with
    `MathUtils.Intersect(AreaUtils.GetTriangle3(nodes, tri).xz, p, out float2 _)`, RaycastJobs.cs:168). A bound run there -> BOUND
    "via point", counted, logged once per run per activation by what the raycast's winner was:
    - another ground area (Area) is an ordinary tie, INFO (every ground-area result keeps HitEntity = terrain with
      m_NormalizedDistance reduced by 0.25/len, RaycastJobs.cs:299-304, so two ground areas at one point tie exactly and
      RaycastResult.Accumulate keeps whichever came first, :14);
    - the terrain is a raycast miss, WARN (the terrain result is t + 1/len, RaycastSystem.cs:177-181, so a validated ground-area
      result t + 0.75/len would have won; RaycastJobs.cs:46-120 accepts our un-owned Surface runs, so the area pass found nothing
      for a run that contains the point: a search-tree / validation anomaly, or a raycast older than the geometry the scan reads,
      RaycastSystem.cs:806-835).
    One of our runs with Owner(sign) and no binding -> LEGACY (WARN once per run per load). Any other run of ours -> UNBOUND (no
    binding / paid orphan whose sign is gone / foreign Owner).
(e) The hit has Area -> FOREIGN surface; (f) else TERRAIN.

## Logging

Silence must be impossible (`GladeHoverLog` in SkiGladeHover.cs): one line per hover-state change, deduplicated on (kind, run or
hit entity, reason), at most `GladeHoverLog.MaxLinesPerActivation` (40) per activation, then one "transitions capped" line,
written right after the Offer that reports CapJustReached, before the Legacy-WARN and Bound-with-re-plan early returns
(CapJustReached is raised on one Offer per activation only). Status strings for the dev tab are built on change only and kept in a
two-slot memo. Stop logs the dwell summary before ResetPreviewState; dwell is fed on every UpdateCore path.

## Lifecycle: emit-and-hold (the vanilla idiom)

Vanilla's object tool emits nothing while the control point is unchanged (ObjectToolSystem.cs:4057-4058) and re-emits only on a
change as Clear + UpdateDefinitions (:4100-4104). Holding works because GenerateObjectsSystem consumes only definitions carrying
Updated (GenerateObjectsSystem.cs:1730-1748), Updated is stripped at frame end (CleanUpSystem.cs:53), and ValidationSystem only
runs on Temp+Updated (ValidationSystem.cs:1917, 1930), so Error/Warning/Override persist while applyMode == None (:645 strips them
when applyMode != None: never hold with Clear). Destroying a definition never removes its Temp; only the ClearTool / ApplyTool
phases do (ToolOutputSystem.cs:22-30). The base never enables the proxies for a mod tool (ToolBaseSystem.cs:362-364;
WasPressedThisFrame is false unless enabled, UIInputAction.cs:62-69), so every frame writes the enable flags; Cancel is enabled
only while a preview is pending, which leaves Escape to the frontend's Back (ToolUISystem.cs:280-304, 344-356).

- Re-plan (hover / target / level / forceUpdate / requested): applyMode = Clear (ToolClearSystem.cs:88-106 deletes our old Temps),
  DestroyDefinitions on the previous frames' definitions (GetDefinitionQuery excludes Updated, ToolBaseSystem.cs:689-692), emit
  the new ones with Updated.
- The frame after an emission the Temps exist and are validated ("ready"). On that frame, before the action flags: the preview
  census, skip-set learning and the after-state hide. An Apply press on that same frame is ignored.
- After an Apply the next ToolUpdate re-plans the same run: the applied trees are in the static tree by then (SearchSystem runs at
  Modification5 of the Apply frame; Deleted trees leave it there, SearchSystem.cs:91-99).

## Apply semantics

`PlanAndEmit` takes a census, then hands `SkiGladeCompose.Compose` the run geometry, level and target, the census minus this run's
members (Existing/Thinnable), this run's members (Own), the known blocked slots (SkipSlots) and the per-apply caps; it emits
Compose's Fell and Remove lists as vanilla bulldoze-shaped delete definitions and its Plant list as planted-tree definitions.
Applying a level twice is a no-op; large runs go in slices of <= 4,000 (provisional caps). This run's trees standing outside their
own layer's region after a run edit are MISPLACED; Compose removes them first. Apply takes the held preview only while it is the
plan of the current level and target (UpdateFrame "planCurrent").

- Skip set: on the ready frame every canceled planted Temp maps back to its slot through m_EmittedSlots (definition positions are
  written verbatim, GenerateObjectsSystem.cs:1253) and joins m_SkipSlots[PlanKey] (<= SkipKeysMax keys, oldest evicted). Lifetime
  is one activation (cleared in OnStartRunning and at load): PlanKey hashes trees, vertex obstacles, exclusion rings and params but
  no road, building or map tile, so a longer-lived set kept a slot skipped after its blocker was removed. Nothing is learned when
  money may be short (PlayerMoney.money < PreviewPlant x MaxTreeCost: ValidationSystem also cancels Optional Temps for money,
  ValidationSystem.cs:1434-1449, not distinguishable per Temp; the state is MoneyShort). A ready preview that learns new slots
  re-plans at once and Apply waits for it: under the count rule a canceled spot leaves the run below T, so otherwise the re-plan
  after the Apply would plant replacements instead of reading "nothing to change".
- Plan cache: `SkiGladeCompose`'s GladePlanCache (PlanCacheEntries = 4, keyed by PlanKey): a saturated giant run burns
  MaxAttemptsPerLayer (200,000) per plan.

## Preview (after-state)

On the ready frame every Delete Temp gets Game.Tools.Hidden + Game.Common.BatchesUpdated through the barrier's ECB, the idiom
vanilla uses on the originals (GenerateObjectsSystem.cs:976-977). The renderer reads the component (BatchDataSystem.cs:605);
Apply reads only the Cancel/Delete flags (ApplyObjectsSystem.cs:337-352) and Delete touches only the original (:484-504);
old-Temp reuse is keyed on (prefab, sub-prefab, original, owner) (GenerateObjectsSystem.cs:56-79, 89-140), so a hidden delete Temp
is only reused for a delete of the same original and the reuse path never removes Hidden. Hold frames fix Delete Temps whose
Hidden state is wrong (self-heal, logged once per plan). Felled trees get a red ring, removed members an orange one (outline only:
a Projected fill would paint every mesh in its column). Warning/Override recoloring is not used: on Apply ToolApplySystem deletes
Warning entities and marks Override ones Overridden + Updated (ToolApplySystem.cs:50-57, :118-126). Fallback if Hidden misbehaves
in the field: the dev tab toggles hiding off; the rings grow to FallbackRingDiameter.

## Definitions

A planted tree is `CreationDefinition{prefab, no owner, stable m_RandomSeed, Optional}` + `ObjectDefinition{terrain-sampled y +
m_PlacementOffset.y, m_IsDecoration = true}` + Updated. A felled / removed tree is the vanilla bulldoze shape
(BulldozeToolSystem.cs:664, 731-751, 812-875); the delete Temp hides each original, which the collision iterator skips
(ValidationHelpers.cs:78), so an apply's new trees never collide with the trees it removes. Both go through ToolOutputBarrier's ECB
(ObjectToolBaseSystem.cs:2446-2451).

## Census

Trees come from SearchSystem's static tree read on the main thread (GetStaticSearchTree(readOnly) + Complete,
SearchSystem.cs:421-425), iterated per run triangle (inside) and per edge (band) with the AreaResourceSystem TreeIterator shape
(AreaResourceSystem.cs:621-660). A tree tagged SkiRunTreeMember with m_Run == this run goes to Own; other runs' members go to
Existing as fixed occupants. Overridden trees are counted too (the iterator requires BoundsMask.IsTree only, not NotOverridden),
separately in m_CenOverriddenInside / Band.

### Why Overridden trees are counted

Trees are Overridable, not DeleteOverridden (every ObjectGeometryPrefab that is not a pillar, utility or net object gets
Overridable | Brushable, ObjectInitializeSystem.cs:578-605; only buildings get DeleteOverridden, BuildingInitializeSystem.cs:859),
so two overlapping trees, or a tree under a net / building, are resolved by OverrideSystem hiding one (Overridden, :1777-1791),
not deleting it; an Overridden object loses BoundsMask.NotOverridden in the static tree (SearchSystem.cs:106, 138-140;
OverrideSystem.cs:74-80, 132-140, 158-161). When a felled tree is Deleted (ApplyObjectsSystem.cs:484-504), UpdateCollectSystem
enqueues its bounds (:66-76), SearchSystem removes it (:90-95) and OverrideSystem re-checks every object in those bounds
(FindUpdatedObjectsJob :180-219 ignores the mask) and removes Overridden from a tree that no longer collides (:1777-1791),
restoring NotOverridden. A census requiring NotOverridden then sees it as a new untagged wild tree and a re-plan right after an
apply reads "plant 5, fell 2".

It is not canceled Temps coming back: a Temp with TempFlags.Cancel on the Apply frame is never created (ApplyObjectsSystem.cs:
337-340, 474-482; the flag is written only by ValidationSystem's ProcessValidationResultsJob.Cancel, :1705-1712, how every error
on an Optional Temp ends, :1667-1703), and every Temp the create path does create is tagged by SkiGladeTagSystem the same
ApplyTool phase (UpdateBefore ApplyObjectsSystem, SystemOrder.cs:714).

Visible consequences (accepted): "Trees now" includes hidden trees; a rank fell can take a hidden tree (red ring, no visible
change) or uncover one (already counted); trees hidden under a road crossing the run count as wild and are felled at low levels.
The tag system mirrors ApplyObjectsSystem's decision order (Cancel -> Delete -> original with PrefabRef -> create); delete Temps
carrying Cancel are not counted as felled because Cancel() keeps their original.

## Apply check

On the Apply frame the tool snapshots the census it planned from, the planted spots validation canceled and the live fell / remove
counts; the first re-plan after the apply re-takes the census of the applied run and writes one "Glade: apply check" line (trees
created vs tagged, own members, non-member trees, new untagged trees classified as "at a canceled spot" / "near a felled tree" /
"elsewhere", vanished trees, un-overridden trees). INFO when every count balances, WARN "APPLY MISMATCH" otherwise. One extra
census per apply.

## Load reset

OnGameLoadingComplete resets the per-load flags and calls ResetPreviewState, the skip sets, the plan cache (entries and hit / miss
counters), the census counters printed by DUMP / the dev tab, the legacy-warned set, the candidate rings, the plan counts the
tooltip shows and a pending apply check.

## Scope and defensive contract

Game mode only; inert (logged) when the run prefabs or the species pool are missing. "created" line, ARMED line per load listing
every constant, one-shot "!!!" catch that disables the tool until the next load, never throws into the engine, no per-frame
managed allocation or string formatting.

## Near EmitPlant

The planted tree: no owner (Owner(run) is reconciled by SubObjectSystem.RelocateSubObjects on any run Updated,
SubObjectSystem.cs:1360-1533). Optional -> TempFlags.Optional (GenerateObjectsSystem.cs:1090-1093): a collision becomes a canceled
Temp instead of an Apply-blocking Error (ValidationHelpers.cs:166-181, ValidationSystem.cs:1636, 1667-1712; vanilla tree brush,
ObjectToolBaseSystem.cs:1024, 1.6.2 :1027), and over-budget Optionals are canceled to fit (ValidationSystem.cs:1434-1449).
m_IsDecoration = true leaves the archetype's Decoration enabled (GenerateObjectsSystem.cs:1311-1317, TreeObject.cs:25-29): the
tree never ages (TreeGrowthSystem.cs:268-273) and counts no wood. y is sampled here because the pipeline never terrain-snaps
(GenerateObjectsSystem.cs:1253; vanilla ObjectToolBaseSystem.SampleTransform :1040-1072, 1.6.2 :1043). The slot is recorded by xz
key: the Temp's Transform is the definition position verbatim (:1253; reuse path :1116), which is how a canceled Temp maps back to
its slot (skip set).

## Quick runs package D (DECISIONS 415)
- The census treats Quick runs filler trees (`LayerFiller`) as wild: a run drawn later over the filler can thin or clear them like the
  map's own trees. Every other tagged tree stays Existing (never thinnable), as before.
- The glade tool implements `IGladeTagSource` (SkiGladeTagSystem.md) with its old Apply-frame guard.
- `EmitPlantDefinition`, `EmitThinDefinition` and `PositionKey` are `internal static` so the tree filler emits exactly the glade tool's
  definitions and skip keys (no copy).
