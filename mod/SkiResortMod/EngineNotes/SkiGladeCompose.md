# SkiGladeCompose: how a glade apply turns a density level into plant, remove and fell lists

The glade tool's apply composition: one tree-density level per layer. `SkiGladeCompose` is pure on purpose (usings are `System`,
`System.Collections.Generic` and `Unity.Mathematics` only), so the file compiles into the net48 mod (default `**/*.cs` glob) and is
linked into the net8 headless host `tests/GladeBatch`, which drives it through a test-local world (`ApplySim`).

## What an apply means

Level L = 1..8 (`Levels`, `DefaultLevel` 4; `input.Level` is clamped, so 0 means 1 = Clear). Two targets: the run interior (layer 0,
`input.TreeLine = false`) and the tree line (layer 1, `TreeLine = true`). An apply on (run R, target, L) brings R's layer to the
level's tree count T.

- Interior: T = round(Area x `InteriorDensity`(L)), wild and planted trees together (round = floor(x + 0.5), Area = the planner's
  sanitized polygon area; read off a planner probe so it uses the planner's own arithmetic).
  - W = wild thinnable trees inside.
  - X = other trees inside the tool may not fell: non-thinnable Existing (foreign-owned, other runs' members, fixed occupants), and
    members of this run with an unknown layer byte, which are kept.
  - M = this run's current interior members inside.
  - N = W + X + M. X holds no member of this run with a known layer.
- Tree line: T = round(`BeltFactor`(L) x B), B = the planner's `RawTargetBelt` = round(`BeltDensity` x `BeltCoverage` x perimeter x
  (`BeltDepth` - `BeltGap`)). N = M = this run's current members in the belt region (outside, within `BeltDepth` + `MinSpacing` of an
  edge: the tool's tree-line census band); W = X = 0: the tree line counts only its own trees and never fells.
- "Current" = `Variant == input.Variant && PlanGen == PlanGeneration` (`Variant` is always 0 for new plantings); every other member of
  the target layer is other-tagged (another variant or generation, v1 tags included).
- Misplaced: a member of either layer of this run standing outside its own layer's region (an interior member outside the polygon, a
  tree-line member inside it, or, for the tree-line target, a current member farther than `BeltDepth` + `MinSpacing` from every edge)
  because a run edit moved the edge past it.
- Other = other-tagged union misplaced members the target's census hands in: removed first of all, before the count rule, wherever
  they stand; neither M nor X; never an occupant of P and never in `PlanKey`. A misplaced member of the other layer is removed by
  whichever target applies first. Correctly placed members of the other layer stay occupants (layer independence).

## The count rule

One branch per apply; an apply never both plants and removes or fells in a layer.

- N > T: keep the first K = clamp(T - W - X, 0, M) members (tree line: clamp(T, 0, M)) in keep order, remove the rest (highest
  first). Only when no member of the layer stands after this apply (K = 0, every removal inside the cap, no Other left) does the
  interior fell wild trees: exactly `FellFull` = W - max(0, T - X) of them, chosen by rank (largest `ThinHash`(position, `ThinSeed`)
  first, ties by x, then z, then Existing index). An exact count, so a repeat fells nothing; a rank, so the set felled at a lower
  level contains the set felled at a higher one. Count after = max(T, X): L = 1 leaves exactly X trees inside.
- N < T: plant D = T - N trees at the first unmatched, unskipped slots of the plan stream (min(D, `MaxPlant`) per apply). No fell and
  no removal in the same apply; while an Other member stands the plant waits (that apply removes Others only and is Clamped).
- N = T: nothing to change (Complete), apart from Other removals.

Keep order: members matched to a slot by slot index; unmatched members after all matched ones, by `ThinHash` ascending (ties x, z, Own
index). So keep = first slots, remove = highest slots. "L up then back down removes exactly the trees the up-step planted" holds only
while the stream is unchanged and the members before the up-step form a prefix of the stream's unskipped slots: a hole below a member
(a bulldozed member, or a blocked slot learned earlier whose blocker is gone; skip sets last one activation) is filled first by the
up-step, and the down-step then keeps that low slot and removes the highest slots, which can be older members. The count is exact
either way. GladeBatch checks the identity only under that precondition.

## The plan stream P

One per layer; level-independent, so `PlanKey`, skip sets and cached plans are shared by all eight levels.

- Interior: the planner's GLADE layer with `GladeDensity` = `StreamDensityPerM2` = 157.3 / ha, 10 % above the level-8 density: at
  exactly level 8's density every learned blocked spot would leave an L8 run short for good, and the headroom lets L8 replace up to
  about 14 blocked spots per hectare from the stream's tail. Its raw target = round(157.3e-4 x Area) - W - X; by the prefix property
  every slot of the 143 / ha stream keeps its index.
- Tree line: the planner's BELT layer (raw = B; a blocked belt spot still leaves L8 short: accepted).
- Seed = `Seed32`(ring[0], `VariantSalt`(Variant)).
- Occupants = every Existing tree, inside and band (nothing is assumed felled) union this run's members of the other layer standing in
  their own region (`SeparationHolds`). This run's members of the target layer are never occupants: that is what makes a repeat a
  no-op. Wild trees are felled only when no member stands, so the stream a member was planted from survives every apply that keeps a
  member.
- P is a prefix of the full stream (planner prefix property). Plant branch: m = Same + |SkipSlots| + min(D, `MaxPlant`) slots. Other
  branches: m = Same + |SkipSlots|, re-planned in full (`FullForMembers`) when an in-region member is unmatched against a slice (a
  bulldozed hole can push a member beyond m).
- Slice ran out of attempts: the planner's attempt budget reads the target, so a clamped slice can stop short ("Saturated" while
  "Clamped"); P is then re-planned with an unbounded `MaxPlant` and cut back to m (`FullReplan`). Cost: on a run with no room a level-8
  plant request runs out of attempts and is re-planned in full (two plans on the first compose of that `PlanKey`; later composes hit
  the tool's plan cache). Planning such requests in full at once is avoided: it makes every unsaturated level-8 compose pack the denser
  tail (+45 % attempts).
- Matching: slot s is matched iff a current in-region member of the layer lies at xz distance < `MinSpacing` / 2. Slots are
  >= `MinSpacing` apart (the planner rejects d < `MinSpacing`), so a member matches at most one slot. Misplaced members never match.

## The probe

One planner call per compose before P: Mode = Thin | (Glade or Belt), `ThinFraction` 1, thinnable = null (all), `MaxPlant` 0,
`GladeDensity` = `InteriorDensity`(L), on Existing ++ Own positions. It validates the geometry as P's call will, returns Area, and
gives (a) `Thinned` = every finite position inside the planner's own polygon test (the inside test for W, X, M and misplaced),
(b) `RawTargetGlade` = floor(`InteriorDensity`(L) x Area + 0.5) = T, (c) `RawTargetBelt` = B. It plants nothing and its status is
Clamped whenever the raw target is > 0 (ignored). Positions beyond +-2.5e8 m (`ThinSelect`'s int range) or non-finite read as
outside: a member there is misplaced (removed), an Existing tree there is not counted.

## ThinHash vs ThinSelect

`ThinHash(pos, seed)` = `SkiGladePlanner.ThinHash` is the full 32-bit hash `ThinSelect` thresholds: `ThinSelect(pos, seed, f)` ==
`((ThinHash(pos, seed) & 0xFFFF) < f x 65536)` for finite, in-range positions. Felling is by rank on the full hash (an exact count,
largest first), not by threshold. Both are pure functions of the 12.5 cm cell, independent of y, of the stand's order and of the
level. Compose does not read `ThinSelect`.

## Flags

- Clamped = more work remains that another apply can do (an Other or count-rule removal cut by `MaxRemove`, a fell cut by `MaxFell` or
  waiting for members, a plant waiting for Others, or a deficit beyond this apply's plants with more plantable slots in the stream).
- Saturated = plant branch, Shortfall > 0 and the full stream ran out of planner attempts ("no room", final; never with Clamped).
- Shortfall = min(D, `MaxPlant`) - |Plant|: the stream has no more plantable slots (learned blocked spots beyond the stream's
  headroom, or at the tree line's raw bound, or no room).
- Complete = Plant, Remove and Fell all empty, which can leave Inside != Target: below it with Shortfall > 0, above it when X > T; the
  tool's tooltip names each case.
- Status = the probe's degenerate status, else Clamped when Clamped, else Ok.

## PlanKey

FNV-1a 64 (format tag "GLK3") of everything P depends on: ring and triangle bits, layer, Variant, `PlanGeneration`, the effective
stream params (every field but `MaxPlant`), obstacles, exclusion rings (order-independent), and the occupants that can affect P; not
the level, the caps or the skip set.

Relevance: the planner rejects only d < `MinSpacing` and for any polygon d(inside point, outside point) >= d_in + d_out.

- Interior: an occupant inside always matters (it blocks slots and counts in the stream's raw); one outside matters only when nearer
  the edge than `MinSpacing` - `InnerMargin`.
- Tree line: one outside always matters; one inside only when nearer the edge than `MinSpacing` - `BeltGap` (inside trees do not
  change B).
- Slack 1 cm (the raw ring vs the planner's sanitized ring, which strips vertices within 1 mm).

Members that cannot matter are still handed to the planner (P is identical); they are only left out of the key, so an interior fell
deep inside keeps the tree line's skip set and cached plan. GladeBatch asserts key soundness on every compose (an equal key always
came with a bit-identical P). The key is never 0.

## Cache

`GladePlanCache` (optional): entries keyed by `PlanKey`, reused when not a slice or holding at least the requested slice (P is then
its prefix, identical to a fresh compute).

## Near Compose (contract)

`Compose(input, output[, cache])` is a pure function of `input` (a cache never changes the result). No static mutable state, no
Dictionary/HashSet iteration feeds the output, never logs, never throws on bad geometry or parameters: a degenerate ring returns the
planner's `GladeStatus` with every output list empty. Runs on re-plan only (never per frame); it allocates. `input.Params` supplies
spacing, margins, belt shape, species, ages and `AttemptsPerTree`; `Params.MinSpacing` must be the target layer's spacing
(`GladeMinSpacing` for the interior, `BeltMinSpacing` for the tree line). `Params.Mode`, `ThinFraction`, `GladeDensity` and `MaxPlant`
are not read (Compose sets them).

## Retired (do not reintroduce)

The Corridor mode, the thin fraction (`ThinSelect` felling), the eight layout variants and variant adoption, and deferral of slots
next to doomed-but-standing trees (a plant never shares an apply with a removal or a fell). `PlanGeneration` stays 1.

## Limits

After a polygon edit, a new or removed occupant inside, or a `PlanGeneration` bump, members stop matching the new stream: the count
rule still holds the count (unmatched members are kept or removed by position hash), but a plant may land within `MinSpacing` of an
off-stream member (the validator may cancel it; the tool learns the slot as blocked).

A removed occupant can also insert a slot: in the belt loop one attempt draws 4 values and an accepted tree 4 more (`SkiGladePlanner`
"Fixed 4 draws per attempt" / "per accepted tree"), so after a newly accepted dart the later darts survive shifted by one index and
the members still match; the next plant fills that new hole first and a later level-down keeps it. The glade loop draws 3 per attempt,
so an insertion there scrambles the later darts instead. A slot learned as blocked stays skipped for its `PlanKey` (one activation).

Compose only sees the members the tool's census hands in (`SkiGladeToolSystem.BandMarginFor`: interior inside + `GladeMinSpacing`
5.5 m, tree line inside + `BeltDepth` + `BeltMinSpacing` 14.5 m), so a member outside both bands stays where it stands until the run
is deleted (`SkiGladeCleanupSystem` deletes every member of a deleted run): one moved far by Move It, interior members of a run
narrowed by more than `InnerMargin` + 14.5 = 17 m, and the outer tree-line members of a run narrowed by more than 14.5 - `BeltDepth`
= 4.5 m.
