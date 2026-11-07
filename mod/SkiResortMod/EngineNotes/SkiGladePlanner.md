# SkiGladePlanner: how the Glade tool's deterministic tree planner works

`SkiGladePlanner.cs` is the pure, deterministic tree planner behind the Glade tool. It is Entities-free on purpose. Its usings are
System, System.Collections.Generic and Unity.Mathematics ONLY. So the same file compiles into the net48 mod AND links into the
net8 headless test host `tests/GladeBatch`. We re-derived it from the vanilla systems named below. Nothing is copied from
ParkManager.

## Contract

The output is a pure function of (ring, triangles, seed, params, obstacles, existing, thinnable, exclusion rings). The planner has these
properties:

- No static mutable state (only consts).
- No Dictionary iteration feeds the output.
- It never logs.
- It never throws on bad geometry or parameters. It returns a `GladeStatus` and leaves both output lists empty for every degenerate
  status.

The tool (`SkiGladeToolSystem`) is the adapter: Node/Triangle buffers in, CreationDefinitions out. It overwrites Position.y with a
terrain sample.

## Phases

The order is fixed. A later layer never moves an earlier one. Each layer has its own salted `Unity.Mathematics.Random` stream. It
consumes a FIXED number of draws per attempt and per accepted tree, so rejection-test order never changes the sequence.

1. Sanitize:
   - NaN/Inf anywhere in the ring -> NonFinite.
   - Strip a duplicated closing vertex and consecutive duplicates (xz within 1 mm).
   - < 3 vertices -> TooFewVertices.
   - |shoelace area| < 1 m2 -> ZeroArea.
   - Validate supplied triangles (index range + total area within 5 % of the polygon) -> BadTriangles.
   - null/empty triangles -> ear clipper over the sanitized ring (BadTriangles if it finds no ear, for example a self-intersecting
     ring).
2. Thin: existing[i] with thinnable[i] (null array = all thinnable, out of range = NOT thinnable, the conservative side) and
   inside the polygon is selected iff `ThinSelect(pos, seed, ThinFraction)`. This is a threshold on a position hash, not a rank. These
   properties follow:
   - The same fraction twice removes nothing new.
   - A larger fraction removes exactly the extra share of the ORIGINAL stand.
   - The result is permutation-invariant.

   Indices come out ascending.
3. Glade:
   - The spacing grid is seeded with every survivor (existing minus thinned, inside or outside).
   - target = round(GladeDensity * Area) - survivorsInside, clamped to [0, MaxPlant] (clamp -> Clamped).
   - Area-CDF triangle pick (binary search) + uniform barycentric sample with reflection. This is the idea of vanilla
     AreaUtils.GetRandomPosition (AreaUtils.cs:249-272, not linkable: it needs DynamicBuffer/Colossal.Mathematics).
   - Reject a candidate in these cases:
     - Outside the polygon.
     - Nearer than InnerMargin to the boundary.
     - Nearer than MinSpacing to a grid occupant.
     - Within obstacle radius + MinSpacing/2.
     - Inside any exclusion ring.
   - Attempts = target * AttemptsPerTree (hard cap MaxAttemptsPerLayer). If the attempts run out -> Saturated.
4. Belt:
   - Pick an edge by length-CDF, t along it, and offset d in [BeltGap, BeltDepth] along the OUTWARD normal (sign from the shoelace
     winding).
   - Reject a candidate in these cases:
     - Inside the polygon.
     - Boundary distance outside [gap, depth].
     - Inside an exclusion ring (other runs).
     - Coverage gate smoothstep(1-cov-0.15, 1-cov+0.15, valueNoise(arc/wavelength)) <= u.
     - The same grid/obstacle checks as the glade.
   - The value noise is periodic around the perimeter (K = max(2, round(perimeter / wavelength)) lattice cells), so the closing vertex
     has no seam.
   - BeltArea = perimeter * (depth - gap).
   - target = round(BeltDensity * BeltCoverage * BeltArea), clamped to the MaxPlant budget left after the glade (MaxPlant is a per-plan
     total).
5. Per tree (4 draws, same layer stream):
   - rotation u * 2pi.
   - species 0 with p = DominantShare, else uniform over 1..SpeciesCount-1.
   - age by the AgeWeights CDF over the four vanilla constants.
   - Position.y = barycentric (glade) / edge lerp (belt).

## Randomness

Streams are `new Unity.Mathematics.Random(hash | 1u)`. Seed 0 is NOT caught by the library in a shipped build. The zero-seed check
is `[Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]` (com.unity.mathematics@1.3.2 random.cs:680-686). A zero xorshift state stays zero
forever.

## Ages

The four constants are vanilla `ToolUtils.GetRandomAge`'s (ToolUtils.cs:148-170): 0f / 0.17500001f / 0.425f / 0.77500004f.
`ObjectUtils.InitializeTreeState` (ObjectUtils.cs:426-454) maps them to Child(<0.1) / Teen(<0.25) / Adult(<0.6) / Elderly(<0.95).

## Precision

All geometry runs in double on the xz plane. The planner evaluates every acceptance test on the FINAL float3 position (after rounding).
So spacing/margin invariants hold for what the tool actually places, also at far map offsets. Cross-runtime bit equality (CoreCLR
test host vs the game's Mono) is unverified. The decision path uses only + - * / sqrt, floor and integer hashes, to make equality
likely.

## Prefix property

`SkiGladeCompose` relies on it. GladeBatch `prefix` asserts it. For fixed inputs, take two runs with MaxPlant m1 and m2 > m1. The accepted trees of the m1 run are a BIT-EXACT PREFIX of the accepted trees of the m2 run (Planted, in order, every field). The reasons:

- The per-attempt acceptance tests never read the target.
- Each layer's stream consumes a fixed number of draws per attempt and per accepted tree.
- The target only decides when the loop stops.

Caveat: the attempt budget DOES read the target (maxAttempts = min(target * AttemptsPerTree, MaxAttemptsPerLayer)). So a clamped
run can stop on its attempt budget with FEWER than m1 trees ("Saturated" while "Clamped"). The result is still a prefix, but shorter than
the slice it was asked for. `SkiGladeCompose` re-plans in full in that case.

## Raw targets and ThinHash

`GladePlan.RawTargetGlade` / `RawTargetBelt` = the unclamped `raw` of the glade and belt targets, floored at 0 and capped at
int.MaxValue (TargetGlade/TargetBelt store the CLAMPED value, which hides the real deficit). `GladePlan.Clamped` = a MaxPlant
budget limited one of the targets (set exactly where ClampTarget sets Status = Clamped). Clear() resets both.

`ThinHash(pos, seed)` exposes the 32-bit position hash that ThinSelect thresholds. The identity `ThinSelect(pos, seed, f) == ((ThinHash(pos, seed)
& 0xFFFF) < f * 65536)` holds for every finite, in-range position and 0 < f < 1 (GladeBatch `thinhash` asserts it). `SkiGladeCompose`
ranks wild trees by it (the count rule fells an EXACT count, largest hash first).

## Fill mask (Quick runs tree filler)

`Plan(..., GladeMask maskOrNull, output)` is an overload. The old signature passes null and is unchanged bit for bit (GladeBatch `pin`
digests, recorded on the pre-filler code). A mask narrows the polygon for the Thin and Glade layers only. The Belt ignores it. The mask
has these effects:

- Allowed(q) means all of these:
  - q is outside every `Holes` ring (crossing number) and at least `HoleMargin` from its edges.
  - With a `Ground` (`GladeHeightField`, an adapter seam the game side implements over its own height sampler): `Ground.HeightAt(q) <= TreeLine`.

  NaN ground reads as not allowed. The planner skips a hole with a non-finite vertex (it never blocks the fill).
- Thin selects only allowed positions.
- survivorsInside counts only allowed ones.
- The Glade target uses `MaskedArea` instead of Area. `MaskedArea` is a cell-center quadrature over the polygon bounds on a `AreaCellM`
  grid (default 2.5 m). The cell grows past 4 M samples.
- The planner rejects a glade dart that is not allowed (last test, no draws). An accepted dart takes y = the ground height.
- The mask consumes no random draws, so the prefix property and replay hold. An empty mask keeps the null-mask stream (GladeBatch
  `filler/emptymask`).
- Cost: the quadrature runs once per Plan call (compose calls Plan twice: probe + stream). 1 km2 with 14 run holes: ~90 ms per compose
  on CoreCLR (GladeBatch TIME `filler.1000m`), 4 km2 ~190 ms (capped slice). Under Mono, expect more (unmeasured).
