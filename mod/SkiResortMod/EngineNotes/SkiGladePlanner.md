# SkiGladePlanner: how the Glade tool's deterministic tree planner works

`SkiGladePlanner.cs` is the pure, deterministic tree planner behind the Glade tool. It is Entities-free on purpose: usings are
System, System.Collections.Generic and Unity.Mathematics ONLY, so the same file is compiled into the net48 mod AND linked into the
net8 headless test host `tests/GladeBatch`. It is re-derived from the vanilla systems named below; nothing is copied from
ParkManager.

## Contract

The output is a pure function of (ring, triangles, seed, params, obstacles, existing, thinnable, exclusion rings). No static
mutable state (only consts), no Dictionary iteration feeds the output, it never logs and never throws on bad geometry or
parameters: it returns a `GladeStatus` and leaves both output lists empty for every degenerate status. The tool
(`SkiGladeToolSystem`) is the adapter: Node/Triangle buffers in, CreationDefinitions out, and it overwrites Position.y with a
terrain sample.

## Phases

Fixed order; a later layer never moves an earlier one. Each layer has its own salted `Unity.Mathematics.Random` stream and
consumes a FIXED number of draws per attempt and per accepted tree, so rejection-test order never changes the sequence.

1. Sanitize: NaN/Inf anywhere in the ring -> NonFinite; strip a duplicated closing vertex and consecutive duplicates (xz within
   1 mm); < 3 vertices -> TooFewVertices; |shoelace area| < 1 m2 -> ZeroArea; supplied triangles validated (index range + total
   area within 5 % of the polygon) -> BadTriangles; null/empty triangles -> ear clipper over the sanitized ring (BadTriangles if
   it finds no ear, e.g. a self-intersecting ring).
2. Thin: existing[i] with thinnable[i] (null array = all thinnable; out of range = NOT thinnable, the conservative side) and
   inside the polygon is selected iff `ThinSelect(pos, seed, ThinFraction)`. A threshold on a position hash, not a rank: the same
   fraction twice removes nothing new, a larger fraction removes exactly the extra share of the ORIGINAL stand, and the result is
   permutation-invariant. Indices come out ascending.
3. Glade: the spacing grid is seeded with every survivor (existing minus thinned, inside or outside); target =
   round(GladeDensity * Area) - survivorsInside, clamped to [0, MaxPlant] (clamp -> Clamped). Area-CDF triangle pick (binary
   search) + uniform barycentric sample with reflection, the idea of vanilla AreaUtils.GetRandomPosition (AreaUtils.cs:249-272;
   not linkable: it needs DynamicBuffer/Colossal.Mathematics). Reject: outside the polygon, nearer than InnerMargin to the
   boundary, nearer than MinSpacing to a grid occupant, within obstacle radius + MinSpacing/2, inside any exclusion ring.
   Attempts = target * AttemptsPerTree (hard cap MaxAttemptsPerLayer); exhausted -> Saturated.
4. Belt: edge by length-CDF, t along it, offset d in [BeltGap, BeltDepth] along the OUTWARD normal (sign from the shoelace
   winding). Reject: inside the polygon, boundary distance outside [gap, depth], inside an exclusion ring (other runs), coverage
   gate smoothstep(1-cov-0.15, 1-cov+0.15, valueNoise(arc/wavelength)) <= u, plus the same grid/obstacle checks. The value noise
   is periodic around the perimeter (K = max(2, round(perimeter / wavelength)) lattice cells) so the closing vertex has no seam.
   BeltArea = perimeter * (depth - gap); target = round(BeltDensity * BeltCoverage * BeltArea), clamped to the MaxPlant budget
   left after the glade (MaxPlant is a per-plan total).
5. Per tree (4 draws, same layer stream): rotation u * 2pi; species 0 with p = DominantShare else uniform over
   1..SpeciesCount-1; age by the AgeWeights CDF over the four vanilla constants; Position.y = barycentric (glade) / edge lerp
   (belt).

## Randomness

Streams are `new Unity.Mathematics.Random(hash | 1u)`. Seed 0 is NOT caught by the library in a shipped build: the zero-seed check
is `[Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]` (com.unity.mathematics@1.3.2 random.cs:680-686), and a zero xorshift state
stays zero forever.

## Ages

The four constants are vanilla `ToolUtils.GetRandomAge`'s (ToolUtils.cs:148-170): 0f / 0.17500001f / 0.425f / 0.77500004f.
`ObjectUtils.InitializeTreeState` (ObjectUtils.cs:426-454) maps them to Child(<0.1) / Teen(<0.25) / Adult(<0.6) / Elderly(<0.95).

## Precision

All geometry runs in double on the xz plane; every acceptance test is evaluated on the FINAL float3 position (after rounding), so
spacing/margin invariants hold for what the tool actually places, also at far map offsets. Cross-runtime bit equality (CoreCLR
test host vs the game's Mono) is unverified; the decision path is kept to + - * / sqrt, floor and integer hashes to make it
likely.

## Prefix property

`SkiGladeCompose` relies on it; asserted by GladeBatch `prefix`. For fixed inputs, the accepted trees of a run with MaxPlant m1
are a BIT-EXACT PREFIX of the accepted trees of a run with MaxPlant m2 > m1 (Planted, in order, every field). The per-attempt
acceptance tests never read the target; each layer's stream consumes a fixed number of draws per attempt and per accepted tree;
the target only decides when the loop stops.

Caveat: the attempt budget DOES read the target (maxAttempts = min(target * AttemptsPerTree, MaxAttemptsPerLayer)), so a clamped
run can stop on its attempt budget with FEWER than m1 trees ("Saturated" while "Clamped"): still a prefix, but shorter than the
slice it was asked for. `SkiGladeCompose` re-plans in full in that case.

## Raw targets and ThinHash

`GladePlan.RawTargetGlade` / `RawTargetBelt` = the unclamped `raw` of the glade and belt targets, floored at 0 and capped at
int.MaxValue (TargetGlade/TargetBelt store the CLAMPED value, which hides the real deficit). `GladePlan.Clamped` = a MaxPlant
budget bound one of the targets (set exactly where ClampTarget sets Status = Clamped). Both are reset by Clear().

`ThinHash(pos, seed)` exposes the 32-bit position hash ThinSelect thresholds: `ThinSelect(pos, seed, f) == ((ThinHash(pos, seed)
& 0xFFFF) < f * 65536)` for every finite, in-range position and 0 < f < 1 (asserted by GladeBatch `thinhash`). `SkiGladeCompose`
ranks wild trees by it (the count rule fells an EXACT count, largest hash first).
