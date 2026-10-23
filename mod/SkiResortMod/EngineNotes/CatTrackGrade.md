# CatTrackGrade: the grading pipeline (path, profile, raster, guards, undo) and the heightmap encoding

The pure half of the Cat Track Grading tool: Unity.Mathematics only, so tests/GradeBatch links `CatTrackGrade.cs` and proves every
rule headless. The engine half is `CatTrackGradeToolSystem` (input, overlay, guards' data, apply) and `HeightmapWriter` (the base
heightmap read / write).

## Pipeline

One solve per change, never per frame.

1. `GradePath.Build`: a tangent-continuous Bezier chain through the clicked points (uniform Catmull-Rom, each span converted to one
   cubic Bezier: controls P1 + (P2 - P0) / 6 and P2 - (P3 - P1) / 6, ends duplicated), resampled every ~`SampleStepM` of arc length
   with an equal step, so the grade limit G * ds is the same everywhere.
2. `GradeProfile.Solve` on the ground under the samples:
   - Follow ground (default, least earthwork): the min-max Lipschitz fit (U + L) / 2, where U is the smallest profile with
     |dy/ds| <= G that is >= the ground and L the largest that is <= it (two O(n) sweeps each); (U + L) / 2 is the G-limited profile
     whose largest cut or fill is smallest. Then a short moving average (`SmoothM`, a convex combination of shifted G-limited
     profiles: still G-limited) and both ends pinned to the ground with slope-G cones (clamping between G-limited bounds keeps the
     limit).
   - Constant grade: the straight ramp between the two end heights.

   A fixed height gain over a fixed path length bounds the grade from below: |dh| / L > G is infeasible (`ShortfallM` more meters
   are needed: a longer line or a switchback). The profile is still produced and its steep part shows red.
3. `GradeRaster.Build`: the cross-section per heightmap texel: flat top `TopWidthM` wide at the profile height, side slopes cut
   1 : `CutRun` (h rises 1 m every `CutRun` m outward) and fill 1 : `FillRun`, each meeting the ground where its line crosses it (so
   the edit blends into the base with no step). Every texel takes the template of its nearest centerline point (two switchback legs
   that pass close never stack), found by per-segment stamping (cost = sum of the segment boxes, not texels x segments).
4. `GradeGuards.Check`: the changed texels (and the flat top) against the obstacles the engine half collected: building lots
   (oriented rects), lift lines (segments + clearance) and water (texels flagged wet). The first hit refuses the grade with its
   kind and place.
5. `GradeUndo`: the pre-image and the written image of each grade (LIFO, capped). Restore writes the pre-image back only where the
   texel still holds what we wrote, so a later foreign edit (the vanilla terrain tool, a newer grade) survives the undo.

## Height encoding (TerrainSystem, 1.6.0f1 decompile)

The base heightmap is R16_UNorm (`SetHeightmap` :3231-3250). World height = `heightScaleOffset.y + u16 / 65535 * heightScaleOffset.x`
(`GetHeightData` :2603-2623: scale.y = (65536 - 1) / heightScale.x, offset = -positionOffset, positionOffset.y =
`heightScaleOffset.y` :2512). Texel (i, j) center = `playableOffset + (i + 0.5, j + 0.5) * playableArea / resolution` (the same
:2620-2622 offset.xz -= 0.5 / scale.xz). `GradeMap` holds that mapping.
