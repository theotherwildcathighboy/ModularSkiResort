# CatTrackGrade: the grading pipeline (path, profile, raster, guards, run outline, undo) and the heightmap encoding

The pure half of the Cat Track Grading tool: Unity.Mathematics only, so tests/GradeBatch links `CatTrackGrade.cs` and proves every
rule headless. The engine half is `CatTrackGradeToolSystem` (input, overlay, data of the guards, apply) and `HeightmapWriter` (the base
heightmap read / write).

## Pipeline

One solve per change, never per frame.

1. `GradePath.Build`: a tangent-continuous Bezier chain through the clicked points (uniform Catmull-Rom, each span converted to one
   cubic Bezier: controls P1 + (P2 - P0) / 6 and P2 - (P3 - P1) / 6, ends duplicated). The chain is resampled every ~`SampleStepM` of
   arc length with an equal step, so the grade limit G * ds is the same everywhere.
2. `GradeProfile.Solve` on the ground under the samples:
   - Follow ground (default, least earthwork): the min-max Lipschitz fit (U + L) / 2. U is the smallest profile with |dy/ds| <= G that
     is >= the ground. L is the largest profile with that limit that is <= the ground (two O(n) sweeps each). (U + L) / 2 is the
     G-limited profile whose largest cut or fill is smallest. Then the fit gets a short moving average (`SmoothM`, a convex combination of
     shifted G-limited profiles: still G-limited). Both ends are pinned to the ground with slope-G cones (clamping between G-limited
     bounds keeps the limit).
   - Constant grade: the straight ramp between the two end heights.

   A fixed height gain over a fixed path length bounds the grade from below: |dh| / L > G is infeasible (`ShortfallM` more meters
   are needed: a longer line or a switchback). The profile is still produced, and its steep part shows red.
3. `GradeRaster.Build`: the cross-section per heightmap texel. The flat top is `TopWidthM` wide (default 20 m since 2026-10-24, 2.5x the
   first 8 m, settings 8-32 m) at the profile height. The side slopes are cut 1 : `CutRun` (h rises 1 m every `CutRun` m outward) and
   fill 1 : `FillRun`. Each slope meets the ground where its line crosses it (so the edit blends into the base with no step). Every
   texel takes the template of its nearest centerline point (two switchback legs that pass close never stack). The code finds it by
   per-segment stamping (cost = sum of the segment boxes, not texels x segments).
   - Crossings (2026-10-25, `keep`): inside the outline of another run (GradeObstacles.Runs), the texel keeps its ground (zone 0,
     counted as KeptTexels). Within `CrossBlendM` 15 m outside it, the template fades into the ground (`KeepWeight`, linear).
   - Measured (GradeBatch GR-20b, 60 m run crossed by a 20 m top): graded straight through, a flat bench flips green 20-24 % runs to
     blue, blue 37 % to black, black 50 % to double black. Kept, the crux of the crossed run moves under 0.1 pp at 15-52 %.
   - `CrossingSteepSample`: inside a crossing the track rides the ground of the run, so a step there steeper than the max grade is the
     guard `CrossingSteep`.
   - Review fixes (2026-10-25):
     - The fade acts only for outlines the path ENTERS (`EnteredOutlines`). A run alongside keeps its own ground and never tilts the
       bench.
     - The profile is pinned to the ground at every sample inside an entered outline or its fade band (`PinMask`, FollowGround only).
       So the template equals the ground wherever the fade acts, and the surface there is the ground's own.
     - `AlongTrackSteepSample` is the net: the FINAL surface (After, bilinear at the path samples) may not step steeper than the max
       grade + 1 pp (bilinear read noise: 0.7 pp) where an outline or its fade reaches. It is also the only protection of a
       ConstantGrade ramp.
   - Measured, GR-21:
     - A 35 % pitch ending 5 m before a run: 39.2 % along the track unpinned and unguarded (now refused: the hillside itself is
       steeper than the limit at the edge).
     - A pitch 45 m before: 30.9 % unpinned, 18.4 % pinned (built).
     - 4 m hollow at the edge, 12 % constant grade: 33.4 % (now 24.7 %, inside the limit).
     - A track 18 m beside a run on a 30 % sidehill: 62 of 712 flat-top texels off the bench, worst 1.01 m (now 0).
   - Inside grading (DECISIONS 420, 2026-10-26): a texel inside a crossed outline is no longer kept. It gets ground + clamp(profile -
     ground under the nearest centerline point, +-InsideMaxM 2 m) x inward fade (depth / InsideFadeM 10 m) x side fade (1 on the flat
     top, falling over InsideSideFadeM 10 m). There is no bench: the cross slope stays the run's. The hold of the profile becomes a
     tolerance band (`PinTolerance`: 0 in the fade band, InsideMaxM x the inward fade inside). `CrossingSteepSample` (ground inside
     steeper than the limit) is gone. The final-surface net decides, so a short steep bump 2 m can flatten builds (GR-22c: 37.5 %
     ground, 7.4 % surface). `ChooseInsideAllowance` walks InsideAllowanceSteps 2 / 1 / 0.5 / 0 with a band check (the tool rates the
     crossed runs, GR-22b: a 40 m 23.5 % green run turns Blue at 2 m and stays Green at 1 m).
4. `GradeGuards.Check` tests the changed texels (and the flat top) against the obstacles that the engine half collected:
   - building lots (oriented rects)
   - lift lines (segments + clearance)
   - water (texels flagged wet)

   The first hit refuses the grade with its kind and place.
5. `GradeUndo`: the pre-image and the written image of each grade (LIFO, capped). Restore writes the pre-image back only where the
   texel still holds what we wrote. So a later foreign edit (the vanilla terrain tool, a newer grade) survives the undo.
6. `GradeRibbon.Build` (2026-10-24): the outline of the cat-track run = the flat top as one closed ring (left edge forward, right edge
   back). Each edge = the centerline offset by half the top width. The loop that an offset makes inside a bend tighter than the half
   width is cut at its crossing (`CutLoops`, window (4 hw + 20 m) / step samples). Then Douglas-Peucker 0.5 m and thinning to 2 m. The
   ring must keep 1.5 m between non-adjacent edges (`Clear`). This is above the InvalidShape distance that the engine uses, snap / 2 = 0.5 m
   for Space (Game.Areas/ValidationHelpers.cs:887-943, AreaUtils.cs:55-58). Otherwise guard `RunShape` applies (switchback legs closer
   than the width). There is no closing duplicate: the engine half adds it (GenerateAreasSystem.cs:282-291). A 1.5 km zigzag at 20 m:
   ~170 nodes, 2 ms.

## Height encoding (TerrainSystem, 1.6.0f1 decompile)

The base heightmap is R16_UNorm (`SetHeightmap` :3231-3250). World height = `heightScaleOffset.y + u16 / 65535 * heightScaleOffset.x`
(`GetHeightData` :2603-2623: scale.y = (65536 - 1) / heightScale.x, offset = -positionOffset, positionOffset.y =
`heightScaleOffset.y` :2512). The center of texel (i, j) = `playableOffset + (i + 0.5, j + 0.5) * playableArea / resolution` (the same
:2620-2622 offset.xz -= 0.5 / scale.xz). `GradeMap` holds that mapping.
