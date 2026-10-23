# SkiGladeTagSystem: tagging planted glade trees before vanilla's Apply makes them permanent

`SkiGladeTagSystem` puts the `SkiRunTreeMember` tag on the trees the glade tool plants, on the Apply frame, before vanilla turns
the Temp trees into permanent ones.

## Mechanism

Based on the 1.6.0f1 decompile; the 1.6.2f1 line is in brackets where it moved.

- Frame order: ToolSystem runs in MainLoop (Game.Common/SystemOrder.cs:58) and does PreTool -> ToolUpdate -> PostTool
  (Game.Tools/ToolSystem.cs:251-258, :327). Inside ToolUpdate, after the active tool's OnUpdate, ToolOutputSystem runs the
  ApplyTool phase only when ToolSystem.applyMode == Apply (Game.Tools/ToolOutputSystem.cs:20-31; registered UpdateAfter in
  ToolUpdate, SystemOrder.cs:694 [695]), and ToolSystem.applyMode is the last-updated tool's applyMode (ToolSystem.cs:147-157).
  So this system ticks on exactly the frame SkiGladeToolSystem set applyMode = Apply, and on Apply frames of every other tool too:
  hence the three-way guard (below).
- Registration (Mod.cs): `UpdateBefore<SkiGladeTagSystem, Game.Tools.ApplyObjectsSystem>(SystemUpdatePhase.ApplyTool)`.
  ApplyObjectsSystem is at ApplyTool (SystemOrder.cs:714 [715]). Our EntityManager change is immediate (main thread) and lands
  before ApplyObjectsSystem schedules its job over Temp+Object (ApplyObjectsSystem.cs:922); its command buffer is the
  ToolOutputBarrier (:1001), played back after ToolOutputSystem (SystemOrder.cs:695 [696]).
- Why the tag survives Apply: for a Temp with m_Original == Null and no Cancel/Delete flag, ApplyObjectsSystem takes the create
  path (:414-432) and Create (:670-687) keeps the same entity: it removes Temp/Animation/BackSide (:672, set :925), adds
  Applied/Created/Updated (:686, set :924) and Recent when m_Cost > 0 (:677-685). Any component we add to the Temp tree is
  therefore on the permanent tree. (ApplyObjectsSystem.cs: dispatch :337, Create :670 and the call :432 are unchanged in 1.6.2f1.)
- Which Temps: exactly the Temps ApplyObjectsSystem will CREATE, decided in its own order (HandleTempEntitiesJob,
  ApplyObjectsSystem.cs:333-438): TempFlags.Cancel first -> Cancel() adds Deleted, nothing is created (:337-340, :474-482); then
  Delete -> the original is deleted (:342-352, :484-504); then an original that HAS PrefabRef -> the Update path changes the
  original and deletes the Temp (:354-413); everything else -> Create keeps the Temp entity (:414-432, :670-687). The query is
  All{Temp, Game.Objects.Tree, PrefabRef} (Tree from the TreeObject archetype, Game.Prefabs/TreeObject.cs:27) without a Deleted
  filter, because ApplyObjectsSystem's own query (Temp + Object, :922) has none. A Temp that already carries Deleted is counted
  ("already deleted") but not tagged: CleanUp destroys it at frame end (Game.Common/CleanUpSystem.cs:48-52) and SearchSystem never
  adds a Deleted chunk (SearchSystem.cs:90-95). While our tool is the active tool it is the only producer of object definitions,
  so every create-path Temp tree is one of our planted trees. The counts of every class are published (LastCreatePath,
  LastTagged, LastCancelled, LastDeleteTemps, LastUpdatePath, LastAlreadyDeleted with LastApplyRun / LastApplyFrame) and the
  tool's APPLY CHECK compares them with the census taken on the next frame.
- Guard: our tool is ToolSystem.activeTool (ToolSystem.cs:84-98), both ToolSystem and our tool report ApplyMode.Apply, and
  `tool.ApplyingRun != Entity.Null` (non-Null only on the frame the tool applies). The activeTool/applyMode checks stop a stale
  ApplyingRun (tool switched away right after an Apply) from tagging a vanilla tool's trees.
- Decoration forced ON (planted trees never age and count no wood). `ObjectDefinition.m_IsDecoration = true` only matters on the
  NEW-entity path: there GenerateObjectsSystem disables Decoration iff !m_IsDecoration (Game.Tools/GenerateObjectsSystem.cs:
  1311-1317 [1.6.2f1 :1314-1316]). The REUSE path (oldEntity != Null, :1112-1219, same line in 1.6.2f1) re-sets
  Transform/Temp/Elevation/Tree state but never touches the Decoration bit, and old Temps are matched by prefab/original/owner
  only (:56-73, :89-140), so a vanilla object-tool ghost of the same species (decorationMode defaults false,
  ObjectToolSystem.cs:2651) can be reused for one of our trees with Decoration DISABLED. We therefore set the bit on every planted
  Temp here; ApplyObjectsSystem.Create keeps the entity (:670-687), so the bit carries into the permanent tree. Decoration is
  IEnableableComponent (Game.Objects/Decoration.cs:8) in the TreeObject archetype (Game.Prefabs/TreeObject.cs:29); TreeGrowthSystem
  only ages trees WithDisabled<Decoration> (TreeGrowthSystem.cs:268-273 [1.6.2f1 :271]). Also done in the dead-run branch
  (untagged trees must not grow either).
  Known limit (not fixable here): the reverse case, where the player switches from a pending glade preview straight to the vanilla
  object tool with the same species and vanilla reuses our Decoration-enabled Temp, leaves that one vanilla tree non-growing.

## What is written

Every planted tree gets `new SkiRunTreeMember(run, tool.ApplyingLayer, tool.ApplyingVariant, SkiGladeCompose.PlanGeneration)`.
Every plan is single-layer (run interior -> layer 0, tree line -> layer 1), so layer and variant are per-plan values read once per
apply. The variant is always the tool's NewPlantingVariant (0): no adoption, no roll; members of another variant / generation
read as "other" and the next apply of the layer removes them.

Guards: a layer other than LayerGlade/LayerBelt tags nothing (an un-removable member with a bogus layer would be worse than a
plain tree), WARN once per load, Decoration still forced on (and the apply check then reports created != tagged). A variant of
VariantUnknown is written as given and WARNed once per load. The layer / VariantUnknown constants of the tag and of the pure
SkiGladeCompose must agree: checked at compile time (LayerContract).

## Output

One census line per Apply: "Glade: tagged N trees for run X - Interior L4 (Open glade), glade layer, variant 0, gen ...;
ApplyObjectsSystem create path N, canceled C (not created), delete D, update U, already deleted K". It is the apply census source
for planted trees, also kept in LastCensus for the dev tab. A create path count that differs from the tagged count is a WARN on
the spot.

## Scope and defensive contract

(SkiRunOwnershipSystem pattern.) Game mode only; ARMED line per load (lists the layout version, PlanGeneration and the
layer/variant constants); m_Armed/m_Disabled/m_ErrorLogged and the warn-once flags reset in OnGameLoadingComplete; one-shot "!!!"
catch that disables tagging until the next load (the tool keeps planting; those trees are simply untagged, i.e. plain trees). No
managed allocation outside Apply frames (OnUpdate returns on the guards first).
