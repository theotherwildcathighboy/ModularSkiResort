# LodgeNumberSystem and SkiLodgeNumber: why same-tier lodges carry a persisted number, and how it is given

Pure rule: `LodgeNumberRule.cs` (tests/BuildingBatch LN1-LN8). Persisted type: `SkiLodgeNumber.cs` (SaveLayouts row `LodgeNumber`,
tests/SaveBatch).

## Why
The hub names a lodge by its tier word ("Car spaces at Chalet"). Two Chalets in one ski area would read the same. So the hub adds a
number while an area has two or more lodges of one tier ("Chalet 1", "Chalet 2"). The number must not change between frames, saves and
loads. A lodge keeps it while the lodge exists and stays in that area.
- Entity order cannot carry it. The game reassigns entity indices at a load. Nothing says a save keeps creation order between
  archetypes (an off-grid lodge has another archetype than one at a road) [UNVERIFIED, not measured]. So the mod stores the number on
  the lodge.
- `SkiLodgeNumber { m_Sign, m_Number }`, layout v1, 9 bytes (version byte, entity, i32). `m_Sign` is the Welcome Sign the number was
  given for. The game remaps it on load like `Owner` (Owner.cs:17, :22). Null after a load means that sign was gone at save time.

## The rule (`LodgeNumberRule.Assign`)
Per (published sign link, `SkiCatalog.Lodges` index):
- A lodge keeps a held number for this same sign. On a clash (two lodges holding one number), the lower entity keeps it.
- Every other lodge (none held, held for another sign, or the clash loser) takes the lowest free number, in entity order.
  In play, lodges arrive one at a time, so this is build order. A bulldozed lodge frees its number. The next new lodge of that tier
  fills it.
- An unlinked lodge gets nothing and keeps a held number untouched. It keeps the number if it links back to the same sign.
- Shown number (`Shown`): the number while the area has two or more of that tier, else 0 (no number shown).

The rule numbers every linked lodge (Hut and Summit too, switched on or not, at a road or not). So "Chalet 2" counts every Chalet of
the area the player sees on the map. Only road lodges have car space rows.

## The system
The system runs in game mode only, after the first recompute of a load (`HasSnapshot`). A pass runs in two cases:
- `SkiResortAttractivenessSystem.Generation` moves (a lodge placed, bulldozed or relinked changes the snapshot).
- Every 256 frames, as a backstop.

A pass reads `CopyTopLevelLinks` and skips Deleted and Temp. It writes only the lodges `Assign` names (AddComponentData or
SetComponentData). It bumps `Revision` when a shown number changes. Phase GameSimulation, not load-bearing. The hub's `lodgeParking`
rows key on `Revision` (contract 30, `number`).

Where the hub names a lodge instance: only the Guests view's car space rows. The receipt's "Best lodge: {NAME}" names the tier that
sets the lodge base, not a building, so it stays unnumbered.

## Logs
- "created".
- ARMED once per load.
- One `LodgeNumber [assign] <tier> <id>: number N in the area of Welcome Sign <id> (had none / had M in
another area / had M, held by a lower entity)` per number given. An old save's lodges get one line each at its first load.
- "!!!" once on a throw (numbers frozen until the next load).
- DUMP: passes, assigned, every shown number.

## Custom names (2026-11-02, contract 31)

A lodge that the player renamed shows that name in the Guests view instead of "Chalet 2". The number stays the fallback. The rename is
the game's rename in the info panel title: `TitleSection.OnRename`, TitleSection.cs:48-52. It writes `NameSystem.SetCustomName`,
NameSystem.cs:214-241.

`LodgeNumberRule.DisplayName` cleans the name:
- Control characters and line separators change to one space.
- It trims the name.
- It allows at most 64 UTF-16 units, the length of the game's own rename field (`maxLength:t=64`).
- It does no other escaping. The game substitutes a placeholder argument as text in one pass.

The hub polls `NameSystem.TryGetCustomName` (NameSystem.cs:209-212) per row, each frame the Guests view observes the rows. It polls
because a rename raises nothing that a system can see (`SkiResortHubUISystem.PollLodgeNames`). Tests LN9-LN12.
