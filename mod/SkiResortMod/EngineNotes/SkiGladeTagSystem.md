# SkiGladeTagSystem: tagging planted glade trees before vanilla's Apply makes them permanent

`SkiGladeTagSystem` puts the `SkiRunTreeMember` tag on the trees the glade tool plants, on the Apply frame, before vanilla turns
the Temp trees into permanent ones.

## Mechanism

Based on the 1.6.0f1 decompile. The 1.6.2f1 line is in brackets where it moved.

- Frame order: ToolSystem runs in MainLoop (Game.Common/SystemOrder.cs:58) and does PreTool -> ToolUpdate -> PostTool
  (Game.Tools/ToolSystem.cs:251-258, :327). Inside ToolUpdate, after OnUpdate of the active tool, ToolOutputSystem runs the
  ApplyTool phase only when ToolSystem.applyMode == Apply (Game.Tools/ToolOutputSystem.cs:20-31, registered UpdateAfter in
  ToolUpdate, SystemOrder.cs:694 [695]). ToolSystem.applyMode is the applyMode of the last-updated tool (ToolSystem.cs:147-157).
  So this system ticks on exactly the frame in which SkiGladeToolSystem set applyMode = Apply. It also ticks on Apply frames of every
  other tool. Hence the three-way guard (below).
- Registration (Mod.cs): `UpdateBefore<SkiGladeTagSystem, Game.Tools.ApplyObjectsSystem>(SystemUpdatePhase.ApplyTool)`.
  ApplyObjectsSystem is at ApplyTool (SystemOrder.cs:714 [715]). Our EntityManager change is immediate (main thread) and lands
  before ApplyObjectsSystem schedules its job over Temp+Object (ApplyObjectsSystem.cs:922). The command buffer of ApplyObjectsSystem is
  the ToolOutputBarrier (:1001). The game plays it back after ToolOutputSystem (SystemOrder.cs:695 [696]).
- Why the tag survives Apply: for a Temp with m_Original == Null and no Cancel/Delete flag, ApplyObjectsSystem takes the create
  path (:414-432). Create (:670-687) keeps the same entity. Create removes Temp/Animation/BackSide (:672, set :925), adds
  Applied/Created/Updated (:686, set :924), and adds Recent when m_Cost > 0 (:677-685). Any component we add to the Temp tree is
  therefore on the permanent tree. (ApplyObjectsSystem.cs: dispatch :337, Create :670 and the call :432 are unchanged in 1.6.2f1.)
- Which Temps: exactly the Temps that ApplyObjectsSystem will CREATE, decided in its own order (HandleTempEntitiesJob,
  ApplyObjectsSystem.cs:333-438):
  1. TempFlags.Cancel first -> Cancel() adds Deleted, nothing is created (:337-340, :474-482).
  2. Then Delete -> the original is deleted (:342-352, :484-504).
  3. Then an original that HAS PrefabRef -> the Update path changes the original and deletes the Temp (:354-413).
  4. Everything else -> Create keeps the Temp entity (:414-432, :670-687).

  The query is All{Temp, Game.Objects.Tree, PrefabRef} (Tree from the TreeObject archetype, Game.Prefabs/TreeObject.cs:27) without a
  Deleted filter, because the own query of ApplyObjectsSystem (Temp + Object, :922) has none. A Temp that already carries Deleted is
  counted ("already deleted") but not tagged. CleanUp destroys it at frame end (Game.Common/CleanUpSystem.cs:48-52), and SearchSystem
  never adds a Deleted chunk (SearchSystem.cs:90-95). While our tool is the active tool, it is the only producer of object definitions,
  so every create-path Temp tree is one of our planted trees. The code publishes the counts of every class (LastCreatePath,
  LastTagged, LastCancelled, LastDeleteTemps, LastUpdatePath, LastAlreadyDeleted with LastApplyRun / LastApplyFrame). The APPLY CHECK of
  the tool compares them with the census taken on the next frame.
- Guard: all three conditions must hold.
  - Our tool is ToolSystem.activeTool (ToolSystem.cs:84-98).
  - Both ToolSystem and our tool report ApplyMode.Apply.
  - `tool.ApplyingRun != Entity.Null` (non-Null only on the frame the tool applies).

  The activeTool/applyMode checks stop a stale ApplyingRun (tool switched away right after an Apply) from tagging the trees of a vanilla
  tool.
- Decoration forced ON (planted trees never age and count no wood). `ObjectDefinition.m_IsDecoration = true` only matters on the
  NEW-entity path: there GenerateObjectsSystem disables Decoration exactly when !m_IsDecoration (Game.Tools/GenerateObjectsSystem.cs:1311-1317
  [1.6.2f1 :1314-1316]). The REUSE path (oldEntity != Null, :1112-1219, same line in 1.6.2f1) re-sets
  Transform/Temp/Elevation/Tree state but never touches the Decoration bit. Old Temps are matched by prefab/original/owner
  only (:56-73, :89-140). So a vanilla object-tool ghost of the same species (decorationMode defaults false,
  ObjectToolSystem.cs:2651) can be reused for one of our trees with Decoration DISABLED. We therefore set the bit on every planted
  Temp here. ApplyObjectsSystem.Create keeps the entity (:670-687), so the bit carries into the permanent tree. Decoration is
  IEnableableComponent (Game.Objects/Decoration.cs:8) in the TreeObject archetype (Game.Prefabs/TreeObject.cs:29). TreeGrowthSystem
  only ages trees WithDisabled<Decoration> (TreeGrowthSystem.cs:268-273 [1.6.2f1 :271]). The code does this in the dead-run branch too
  (untagged trees must not grow either).
  Known limit (not fixable here): the reverse case. The player switches from a pending glade preview straight to the vanilla
  object tool with the same species, and vanilla reuses our Decoration-enabled Temp. That leaves that one vanilla tree non-growing.

## What is written

Every planted tree gets `new SkiRunTreeMember(run, tool.ApplyingLayer, tool.ApplyingVariant, SkiGladeCompose.PlanGeneration)`.
Every plan is single-layer (run interior -> layer 0, tree line -> layer 1), so layer and variant are per-plan values read once per
apply. The variant is always the tool's NewPlantingVariant (0): no adoption, no roll. Members of another variant / generation
read as "other", and the next apply of the layer removes them.

Guards: a layer other than LayerGlade/LayerBelt tags nothing (an un-removable member with a bogus layer would be worse than a
plain tree). It gives a WARN once per load, and Decoration is still forced on (the apply check then reports created != tagged). A variant of
VariantUnknown is written as given and WARNed once per load. The layer / VariantUnknown constants of the tag and of the pure
SkiGladeCompose must agree. The check runs at compile time (LayerContract).

## Output

One census line per Apply: "Glade: tagged N trees for run X - Interior L4 (Open glade), glade layer, variant 0, gen ...;
ApplyObjectsSystem create path N, canceled C (not created), delete D, update U, already deleted K". It is the apply census source
for planted trees. The system also keeps it in LastCensus for the dev tab. A create path count that differs from the tagged count is a WARN on
the spot.

## Scope and defensive contract

(SkiRunOwnershipSystem pattern.)

- Game mode only.
- One ARMED line per load (it lists the layout version, PlanGeneration and the layer/variant constants).
- m_Armed/m_Disabled/m_ErrorLogged and the warn-once flags reset in OnGameLoadingComplete.
- One-shot "!!!" catch that disables tagging until the next load (the tool keeps planting, and those trees are simply untagged, i.e. plain trees).
- No managed allocation outside Apply frames (OnUpdate returns on the guards first).

## Two sources, one tag system (DECISIONS 415, Quick runs package D)
The planting tool is read through `IGladeTagSource` (ApplyingNow, ApplyingRun, ApplyingLayer, ApplyingVariant, OnTagged). Two tools
implement it:
- The glade tool (ApplyingNow = ApplyingRun != Null, the old guard unchanged. OnTagged is empty).
- The tree filler of the Quick runs tool (run Null, `LayerFiller`, variant 0. OnTagged records the planted batch for its undo and learns skip slots).

The tool that is `activeTool` and reports its Apply frame is tagged. There is no second tag system. A filler tag (LayerFiller, no run) is
never an orphan (SkiGladeCleanupSystem), and no run delete reaches it.
