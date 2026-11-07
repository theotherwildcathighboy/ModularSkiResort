# SkiGladeCompose: how a glade apply turns a density level into plant, remove and fell lists

The glade tool's apply composition: one tree-density level per layer. `SkiGladeCompose` is pure on purpose (usings are `System`,
`System.Collections.Generic` and `Unity.Mathematics` only). So the file compiles into the net48 mod (default `**/*.cs` glob) and is
linked into the net8 headless host `tests/GladeBatch`. The host drives it through a test-local world (`ApplySim`).

## What an apply means

Level L = 1..8 (`Levels`, `DefaultLevel` 4). `input.Level` is clamped, so 0 means 1 = Clear. Two targets: the run interior (layer 0,
`input.TreeLine = false`) and the tree line (layer 1, `TreeLine = true`). An apply on (run R, target, L) brings R's layer to the
level's tree count T.

- Interior: T = round(Area x `InteriorDensity`(L)), wild and planted trees together. Here round = floor(x + 0.5). Area = the planner's
  sanitized polygon area, read off a planner probe so it uses the planner's own arithmetic.
  - W = wild thinnable trees inside.
  - X = other trees inside that the tool may not fell. These are non-thinnable Existing trees (foreign-owned, other runs' members, fixed
    occupants), and members of this run with an unknown layer byte (the tool keeps these).
  - M = this run's current interior members inside.
  - N = W + X + M. X holds no member of this run with a known layer.
- Tree line: T = round(`BeltFactor`(L) x B). B = the planner's `RawTargetBelt` = round(`BeltDensity` x `BeltCoverage` x perimeter x
  (`BeltDepth` - `BeltGap`)). N = M = this run's current members in the belt region (outside, within `BeltDepth` + `MinSpacing` of an
  edge: the tool's tree-line census band). W = X = 0: the tree line counts only its own trees and never fells.
- "Current" = `Variant == input.Variant && PlanGen == PlanGeneration` (`Variant` is always 0 for new plantings). Every other member of
  the target layer is other-tagged (another variant or generation, v1 tags included).
- Misplaced: a member of either layer of this run that stands outside its own layer's region because a run edit moved the edge past it.
  The cases are:
  - an interior member outside the polygon,
  - a tree-line member inside the polygon,
  - for the tree-line target, a current member farther than `BeltDepth` + `MinSpacing` from every edge.
- Other = the union of the other-tagged members and the misplaced members that the target's census hands in. The tool removes Other first
  of all, before the count rule, wherever they stand. Other is neither M nor X. It is never an occupant of P and never in `PlanKey`.
  Whichever target applies first removes a misplaced member of the other layer. Correctly placed members of the other layer stay
  occupants (layer independence).

## The count rule

One branch per apply. An apply never both plants and removes or fells in a layer.

- N > T: keep the first K = clamp(T - W - X, 0, M) members (tree line: clamp(T, 0, M)) in keep order. Remove the rest (highest first).
  The interior fells wild trees only when no member of the layer stands after this apply. That means K = 0, every removal inside the cap, no Other
  left. Then it fells exactly `FellFull` = W - max(0, T - X) of them. The tool chooses them by rank (largest `ThinHash`(position,
  `ThinSeed`) first, ties by x, then z, then Existing index). The count is exact, so a repeat fells nothing. The choice is by rank, so the
  set felled at a lower level contains the set felled at a higher one. Count after = max(T, X): L = 1 leaves exactly X trees inside.
- N < T: plant D = T - N trees at the first unmatched, unskipped slots of the plan stream (min(D, `MaxPlant`) per apply). No fell and no
  removal in the same apply. While an Other member stands, the plant waits (that apply removes Others only and is Clamped).
- N = T: nothing to change (Complete), apart from Other removals.

Keep order: members matched to a slot by slot index. Unmatched members come after all matched ones, by `ThinHash` ascending (ties x, z, Own
index). So keep = first slots, remove = highest slots. "L up then back down removes exactly the trees the up-step planted" holds only
while two conditions hold:
- The stream is unchanged.
- The members before the up-step form a prefix of the stream's unskipped slots.

A hole below a member breaks the second condition. The up-step fills that hole first. The down-step then keeps that low slot and removes
the highest slots, which can be older members. Two cases make a hole: a bulldozed member, or a blocked slot learned earlier whose blocker
is gone. Skip sets last one activation. The count is exact either way. GladeBatch checks the identity only under that precondition.

## The plan stream P

One per layer. It is level-independent, so `PlanKey`, skip sets and cached plans are shared by all eight levels.

- Interior: the planner's GLADE layer with `GladeDensity` = `StreamDensityPerM2` = 157.3 / ha, 10 % above the level-8 density. At exactly
  level 8's density, every learned blocked spot would leave an L8 run short for good. The headroom lets L8 replace up to about 14 blocked
  spots per hectare from the stream's tail. Its raw target = round(157.3e-4 x Area) - W - X. By the prefix property, every slot of the
  143 / ha stream keeps its index.
- Tree line: the planner's BELT layer (raw = B). A blocked belt spot still leaves L8 short: accepted.
- Seed = `Seed32`(ring[0], `VariantSalt`(Variant)).
- Occupants = every Existing tree, inside and band (nothing is assumed felled), plus this run's members of the other layer standing in
  their own region (`SeparationHolds`). This run's members of the target layer are never occupants: that is what makes a repeat a no-op.
  The tool fells wild trees only when no member stands, so the stream a member was planted from survives every apply that keeps a member.
- P is a prefix of the full stream (planner prefix property). Plant branch: m = Same + |SkipSlots| + min(D, `MaxPlant`) slots. Other
  branches: m = Same + |SkipSlots|. The tool re-plans in full (`FullForMembers`) when an in-region member is unmatched against a slice (a
  bulldozed hole can push a member beyond m).
- Slice ran out of attempts: the planner's attempt budget reads the target, so a clamped slice can stop short ("Saturated" while
  "Clamped"). P is then re-planned with an unbounded `MaxPlant` and cut back to m (`FullReplan`). Cost: on a run with no room, a level-8
  plant request runs out of attempts and is re-planned in full. That gives two plans on the first compose of that `PlanKey`. Later composes hit
  the tool's plan cache. The tool does not plan such requests in full at once, because that makes every unsaturated level-8 compose pack
  the denser tail (+45 % attempts).
- Matching: slot s is matched iff a current in-region member of the layer lies at xz distance < `MinSpacing` / 2. Slots are
  >= `MinSpacing` apart (the planner rejects d < `MinSpacing`), so a member matches at most one slot. Misplaced members never match.

## The probe

One planner call per compose before P. The call uses Mode = Thin | (Glade or Belt), `ThinFraction` 1, thinnable = null (all),
`MaxPlant` 0 and `GladeDensity` = `InteriorDensity`(L), on Existing ++ Own positions. It validates the geometry as P's call will, and it
returns Area. It also gives:
- (a) `Thinned` = every finite position inside the planner's own polygon test (the inside test for W, X, M and misplaced).
- (b) `RawTargetGlade` = floor(`InteriorDensity`(L) x Area + 0.5) = T.
- (c) `RawTargetBelt` = B.

The probe plants nothing. Its status is Clamped whenever the raw target is > 0 (ignored). Positions beyond +-2.5e8 m (`ThinSelect`'s int
range) or non-finite read as outside. A member there is misplaced (removed). An Existing tree there is not counted.

## ThinHash vs ThinSelect

`ThinHash(pos, seed)` = `SkiGladePlanner.ThinHash` is the full 32-bit hash that `ThinSelect` thresholds: `ThinSelect(pos, seed, f)` ==
`((ThinHash(pos, seed) & 0xFFFF) < f x 65536)` for finite, in-range positions. Felling is by rank on the full hash (an exact count, largest
first), not by threshold. Both are pure functions of the 12.5 cm cell. They are independent of y, of the stand's order and of the level.
Compose does not read `ThinSelect`.

## Flags

- Clamped = more work remains that another apply can do. This is one of these cases:
  - an Other or count-rule removal cut by `MaxRemove`,
  - a fell cut by `MaxFell` or waiting for members,
  - a plant waiting for Others,
  - a deficit beyond this apply's plants, with more plantable slots in the stream.
- Saturated = plant branch, Shortfall > 0 and the full stream ran out of planner attempts ("no room", final, never with Clamped).
- Shortfall = min(D, `MaxPlant`) - |Plant|. The stream has no more plantable slots. This happens with learned blocked spots beyond the stream's
  headroom, or at the tree line's raw bound, or with no room.
- Complete = Plant, Remove and Fell all empty. This can leave Inside != Target: below it with Shortfall > 0, above it when X > T. The
  tool's tooltip names each case.
- Status = the probe's degenerate status, else Clamped when Clamped, else Ok.

## PlanKey

FNV-1a 64 (format tag "GLK3") of everything P depends on:
- ring and triangle bits,
- layer, Variant, `PlanGeneration`,
- the effective stream params (every field but `MaxPlant`),
- obstacles,
- exclusion rings (order-independent),
- the occupants that can affect P.

The key does not include the level, the caps or the skip set.

Relevance: the planner rejects only d < `MinSpacing` and for any polygon d(inside point, outside point) >= d_in + d_out.

- Interior: an occupant inside always matters (it blocks slots and counts in the stream's raw). One outside matters only when nearer
  the edge than `MinSpacing` - `InnerMargin`.
- Tree line: one outside always matters. One inside matters only when nearer the edge than `MinSpacing` - `BeltGap` (inside trees do not
  change B).
- Slack 1 cm (the raw ring vs the planner's sanitized ring, which strips vertices within 1 mm).

The tool still hands members that cannot matter to the planner (P is identical). It only leaves them out of the key, so an interior fell
deep inside keeps the tree line's skip set and cached plan. GladeBatch asserts key soundness on every compose (an equal key always
came with a bit-identical P). The key is never 0.

## Cache

`GladePlanCache` (optional): entries keyed by `PlanKey`. The cache reuses an entry when it is not a slice or holds at least the requested
slice. P is then its prefix, identical to a fresh compute.

## Near Compose (contract)

- `Compose(input, output[, cache])` is a pure function of `input` (a cache never changes the result).
- It has no static mutable state.
- No Dictionary/HashSet iteration feeds the output.
- It never logs.
- It never throws on bad geometry or parameters: a degenerate ring returns the planner's `GladeStatus` with every output list empty.
- It runs on re-plan only (never per frame). It allocates.
- `input.Params` supplies spacing, margins, belt shape, species, ages and `AttemptsPerTree`.
- `Params.MinSpacing` must be the target layer's spacing (`GladeMinSpacing` for the interior, `BeltMinSpacing` for the tree line).
- Compose does not read `Params.Mode`, `ThinFraction`, `GladeDensity` or `MaxPlant` (Compose sets them).

## Retired (do not reintroduce)

- The Corridor mode.
- The thin fraction (`ThinSelect` felling).
- The eight layout variants and variant adoption.
- Deferral of slots next to doomed-but-standing trees (a plant never shares an apply with a removal or a fell).

`PlanGeneration` stays 1.

## Limits

After a polygon edit, a new or removed occupant inside, or a `PlanGeneration` bump, members stop matching the new stream. The count rule
still holds the count (the tool keeps or removes unmatched members by position hash). But a plant may land within `MinSpacing` of an
off-stream member (the validator may cancel it, and the tool learns the slot as blocked).

A removed occupant can also insert a slot. In the belt loop, one attempt draws 4 values and an accepted tree 4 more (`SkiGladePlanner`
"Fixed 4 draws per attempt" / "per accepted tree"). So after a newly accepted dart, the later darts survive shifted by one index and
the members still match. The next plant fills that new hole first, and a later level-down keeps it. The glade loop draws 3 per attempt,
so an insertion there scrambles the later darts instead. A slot learned as blocked stays skipped for its `PlanKey` (one activation).

Compose only sees the members the tool's census hands in (`SkiGladeToolSystem.BandMarginFor`: interior inside + `GladeMinSpacing`
5.5 m, tree line inside + `BeltDepth` + `BeltMinSpacing` 14.5 m). So a member outside both bands stays where it stands until the run
is deleted (`SkiGladeCleanupSystem` deletes every member of a deleted run). Three cases:
- A member moved far by Move It.
- Interior members of a run narrowed by more than `InnerMargin` + 14.5 = 17 m.
- The outer tree-line members of a run narrowed by more than 14.5 - `BeltDepth` = 4.5 m.

## Filler (Quick runs tree filler)

`ComposeInput.Fill` (a `GladeMask`) with `TreeLine = false` selects layer `LayerFiller` (2). Everything above applies with interior
semantics (`IsInterior(layer)` = not the belt), with these differences:

- The region is the polygon (the drawn Quick runs region) narrowed by the mask (run outlines + margin, tree line). T = round(D(L) x
  MaskedArea). The probe and the stream both get the mask.
- The filler never fells: W = 0, and every wild tree in the fill region counts in X, whatever `Thinnable` says. Level 1 (Clear)
  therefore removes exactly this fill's current members. That is the filler's undo (GladeBatch `filler/compose` "FILL UNDO").
- The tool never classifies own members of another layer (a run's glade or tree-line members handed in by mistake). They are occupants,
  and X when inside. The tool never removes them. A glade compose (Fill null) reads a filler member the same way (unknown layer).
- Misplaced = a filler member outside the fill region. A lowered tree line, or a run drawn later over it, removes those members first.
- Every occupant is keyed (`Matters` is conservative for the filler). The tool hashes the mask (holes order-independent, margin, tree
  line, ground `Fingerprint`, area cell) into PlanKey only for the filler layer. So glade / tree-line keys are unchanged.
- A tree-line compose ignores Fill (same key with and without).
- Species: `SkiGladeSpecies.ApplyPool` sets SpeciesCount / DominantShare. Species draws come after the position, so changing the pool
  never moves a slot. The tool keeps the members of the old pool, and only new plants use the new pool.
