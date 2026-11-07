# GladeBatch.ApplySim: how the glade apply harness works and what it asserts

`ApplySim` is the apply-sequence harness for the glade tool's compose / apply loop.

## The test-local world

Trees are wild, foreign (other runs' members or owned trees) or this run's members. Members are tagged `{layer, variant, planGen}`. Each fixture
has one run polygon. `ApplyOnce` mirrors the tool's apply loop:

1. Oracle census: a winding-number inside test plus an own segment distance, with the tool's census bands (interior =
   `GladeMinSpacing`, tree line = `BeltDepth + BeltMinSpacing`). `Existing` is sorted by (x, z, id) like the tool.
2. `PlanKey`, then the skip set learned for that key.
3. `Compose(level, target)`.
4. A stand-in validator cancels planted trees inside a 12 m blocked strip across the run. It also cancels a plant within 4 m of a tree still standing. The harness learns canceled slots into the skip set, like the tool's first ready preview frame.
5. Mutate the world: fell, remove, plant members tagged layer / variant 0 / `PlanGeneration`.

## Oracles that do not restate the implementation

An independent recount judges every compose (`CheckComposeOracles`). The test recomputes these values:

- W / X / M / N / Others from the test-local census geometry.
- T from the documented table typed in here and the test's own shoelace and perimeter.
- The plan stream, re-derived from its definition, with P required to be its bit-exact prefix. The definition has: planner glade/belt layer, variant-0 seed, occupants = every census tree plus other-layer members, the level-8 density.
- Member-to-slot matching by a sorted-x sweep.
- The keep order, the removal sequence, the exact wild-fell rank set (a test-local `ThinHash` restating the documented formula), the plant
  slots, and Clamped / Saturated / Shortfall.

World state then judges every apply (`ApplyOnce`): which ids disappeared, which positions appeared, the recounted tree count.

## Assertions

- never both: an apply never plants AND removes/fells in a layer (compose and world).
- members first: a wild tree is felled only when no tree of this run stands in the interior census (any layer inside, interior strays
  within `GladeMinSpacing` outside).
- exact rank: the felled set is exactly the top-k by `ThinHash` rank, k = W - max(0, T - X).
- nested: felled sets are nested in L for the same stand (levels / nested).
- count: after an un-clamped apply the count is T (interior: max(T, X) when too many), except blocked spots / no room (Shortfall) and
  validator cancels.
- L1: level 1 leaves exactly X trees inside and NO tree of this run in the target's census (every own member, any layer, any tag: the
  same rule as Compose).
- misplaced: this run's members outside their own layer's region are Other for every target whose census sees them: removed first,
  never X / M / occupants.
- twice: applying L again after it settled is a no-op.
- up/down: raising L only plants, lowering only removes/fells. L up then back down removes exactly the trees the up-step planted (stream
  unchanged and the members a prefix of its unskipped slots, see the `SkiGladeCompose` header).
- plant set: a plant sequence plants exactly the first D unmatched, unblocked slots of the stream (blocked spots are compensated by later
  slots).
- layers: an apply never touches a correctly placed member of the other layer or a foreign tree (it may remove only the other layer's
  misplaced census members). The tree line never fells. A tree-line apply never changes the interior key.
- keybook: on EVERY compose, an equal `PlanKey` always came with a bit-identical P prefix.
- reused result: EVERY compose without a cache is repeated into ONE long-lived `ComposeResult`. It must equal the fresh-object compose
  field by field. The tool reuses one result, so a stale `FellFull` / `FullForMembers` must not leak.
- caps: run800k has plant and remove slices of 4,000, members before wild across slices, and a fell cap fells the exact rank order
  slice by slice.
