# SkiSyncSystem: Sync Resort (1.0.2, T229, DECISIONS 670)

Files: `SkiSyncCatalog.cs` (the step list and its pure rules, tests/ResortBatch SY-1 to SY-6), `SkiSyncSystem.cs` (the engine),
`SkiSyncOverlaySystem.cs` (the world rings), `SkiResortHubUISystem.Sync.cs` and `SkiResortUI/src/hub/SyncCard.tsx` (the hub card).
The level an object was last checked at is its `SkiRulesStamp` (EngineNotes/SkiRulesStampSystem.md).

## The rules (owner, 670)
- A patch that changes how built objects should look adds a numbered step. Numbers are never reused. A retired step stays as a no-op.
- Each step has its own "needs it?" check. The stamp only says which steps were already checked. The check is the truth.
- Auto steps fix what the player cannot see as a loss. They run by themselves after a load.
- Offer steps change what the player sees. They wait for the hub's Sync button. The card has a per-item skip and "Keep as is" per item.
  Sync is free and has no undo.
- A step that re-plans lift towers skips lines edited by hand (`SkipsHandEdited`). It lists them as skipped.
- A stamp newer than this build (a save from a later version) is left alone.

## Steps in 1.0.2
| # | Id | Class | Acts on | Needs it? | Action |
|---|---|---|---|---|---|
| 1 | patrolParts | Offer | Patrol Hut, Patrol HQ | a part row of the prefab has no placed part (the 1.0.1 firewood pile) | `{Event, SubObjectsUpdated(building)}` |

There is no tower step in 1.0.2. The planner did not change, so no line needs a re-plan. The hand-edit marker ships now, so 1.0.2 saves
already carry it when a tower step comes.

## Flow
1. Intake, once per load. It starts after the stamp system's load census and after the area links are known. If the links are not known
   after `LinkWaitFrames` (1200), it starts anyway. It reads only the kinds that some step acts on, `IntakePerFrame` (64) per frame.
   - Nothing needed: the object is stamped Current without a word.
   - Auto step needed: queued at once.
   - Offer step needed: listed under its ski area's "Sync resort (N)" button.
   - Hand-edited line, step skips those: listed as skipped.
2. Run. The Sync button queues the checked rows of the shown area. The queue starts `ApplyPerFrame` (8) objects per frame. It holds while
   any tool other than the default tool is active.
3. Check. `VerifyFrames` (10) after the action, the step's own check runs again.
   - Still needed: the object failed. One WARN line. It keeps its level. No retry until the next load.
   - Done: the next needed step runs, or the object is stamped Current.

## Step 1: why a part needs a rebuild
A load does not add a part that the prefab gained later. `Game.Serialization.SubObjectSystem` rebuilds a building's part buffer from the
saved parts only. Game check (r2-sync, 1.0.1 pack): Patrol Huts placed under 1.0.0 have no firewood pile after a load.
This is true in the migrated 1.0.0 baseline and in a player save. After Sync each hut keeps its 8 parts (same entities) and gains one FirewoodPile01. After a save and
a reload the huts read level 1 and nothing is offered.

The action is vanilla's own rebuild of one owner's parts: an entity with `Game.Common.Event` and `Game.Objects.SubObjectsUpdated`
(WetnessSystem.cs:93-94 in 1.6.4f1). `Game.Objects.SubObjectSystem` at Modification2B processes it:
- It reuses each placed part of a matching prefab, nearest first (`FindOldSubObject`).
- It adds the missing parts and deletes the parts the prefab no longer has (SubObjectSystem.cs:1172-1260, :3307-3365).
- So a rebuild makes no duplicates. The `Sync [done]` line still logs the parts beyond the prefab rows (expected 0).

The event entity is destroyed at the frame's cleanup. So the engine runs at Modification1, before Modification2B (Mod.cs, load-bearing).

A row counts for the check only when vanilla always places it (`SkiSyncCatalog.RowCounts`):
- probability 100,
- no Require* placement flag,
- the part prefab has no `ObjectRequirementElement` (weather, theme).
- A placeholder row (`PlaceholderObjectElement`: vanilla places one of its variants) counts when one variant has no object requirement.
  A placed part of any variant satisfies it. The firewood row is such a row (`FirewoodPileRandom01` places `FirewoodPile01`).

## The card and the rings
- The Overview shows "Sync resort (N)" only while the shown area has listed objects. The card replaces the Overview's sections.
- A view switch, an area switch or closing the hub closes the card.
- While the card is open, `SkiSyncOverlaySystem` draws a projected ring around each listed object of the area. Amber = waiting for the
  player. White = queued or working. Red = failed. Gray = skipped (unchecked or edited by hand).
- No icon prefab: rings need no asset and vanish with the card.

## Logs
- `Sync [assets]` once per load: every building of ours whose placed parts miss rows of its prefab, by building and part prefab. This
  line answers "does an asset change reach placed buildings by itself".
- `Sync [intake]` once per load: objects checked, stamped silently, listed, queued, skipped.
- `Sync [run]` per Sync, `Sync [keep]` per "Keep as is", `Sync [done]` or WARN `Sync [failed]` per object.
- One `!!!` exception line turns the engine off until the next load. Nothing else changes.

## Cost
The intake reads our objects only (tens to a few hundred), 64 per frame, once per load. A Sync starts 8 objects per frame. Vanilla's
part rebuild for 8 buildings is one small batch at Modification2B.
