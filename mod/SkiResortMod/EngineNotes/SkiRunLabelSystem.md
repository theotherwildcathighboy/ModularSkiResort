# SkiRunLabelSystem: how run names are drawn flat on the ground, reusing the district-label machinery (Game.dll 1.6.0f1 baseline)

Each of our runs gets its name written flat on the ground at the run's center, in the piste-map color of its difficulty, at the
zoom range where the piste map reads, faded in and out like the district names. Default on (option `ShowRunNamesOnTheHill`).

## Which mechanism

Vanilla's district / street names are NOT one reusable "label" service:

* Streets (`Game.Rendering/AggregateMeshSystem`, `NetLabel` prefab) and districts (`AreaBufferSystem` name mesh, `DistrictPrefab`)
  each build a per-entity `LabelVertex` buffer (a TextMeshPro text mesh, `AreaBufferSystem.UpdateLabelVertices` :1010-1196) and draw it
  with a per-area-type NAME material (`AreaTypePrefab.m_NameMaterial`, set up :816-821; drawn `AreaRenderSystem` :81-100).
* The area path cannot be pointed at a run: runs are Surface areas; the `LabelVertex` / `LabelExtents` buffers exist only in the
  `DistrictPrefab` archetype (DistrictPrefab.cs:30-31); `UpdateLabelVertices` walks EVERY area of the type and indexes the buffer
  accessor (:1033-1036), which would need the buffer on every building's grass sub-area; the color is one `AreaNameData` per PREFAB
  (:481-492), not per run; and the name material belongs to the vanilla Surface `AreaTypePrefab`.
* `OverlayRenderSystem.Buffer.DrawText(entity, pos, cameraFace)` (:189-198) is the public text path (`GuideLinesSystem` uses it for
  water-source names), but it has no color, no scale (the matrix is TRS(pos, identity, 1), :193) and no ground orientation.

## What is reused

This system builds its labels the way those two do and reuses everything below the per-entity buffer:

* The game's own TextMeshPro text mesh and font: `OverlayRenderSystem.GetTextMesh()` (:641), the same size / alignment settings
  (:708-712, `AreaBufferSystem` :1017-1019), the same mesh copy (`FillMeshFromTMPMeshInfo` :674-698).
* The game's own overlay TEXT material (`OverlayConfigurationPrefab.m_TextMaterial`, as `GetOverlayTextMaterial` :1147-1153) with the
  font atlas copied across (`OverlayRenderSystem.CopyFontAtlasParameters` :667-672).
* The district labels' geometry: `AreaUtils.CalculateLabelRotation` (flat on the ground, reads along the camera's right,
  AreaUtils.cs:115-119) and `AreaUtils.CalculateLabelScale` (sqrt(distance x 0.001), :126-129): the same size-by-distance rule.
* The district labels' anchor: `Geometry.m_CenterPosition` (`AreaUtils.CalculateLabelPosition` :121-124). `GeometrySystem` computes it
  as the point INSIDE the area that is furthest from every edge (Game.Areas/GeometrySystem.cs:520-585, the best
  `CheckCenterPositionCandidate`), so a concave run's label never lands outside it. The catalog row already carries it
  (`RunRow.CenterX` / `CenterZ`); y is the terrain height + `LiftM`.

What is ours: the per-run mesh (color and fade in the vertex colors), the fade rule (`RunLabelRules.FadeAlpha`; the vanilla district
fade is inside its shader, which the decompile does not show), and the draw (`Graphics.DrawMesh`, the camera of the game).

## Gates (cheapest first)

Option `ShowRunNamesOnTheHill`; Game mode armed; `RenderingSystem.hideOverlay` (photo mode / screenshot hides the district names the
same way, `AreaRenderSystem` :81); the fade (zoom outside the range: nothing, nothing rebuilt); a tool working on runs (the area tool
holding one of our run prefabs or the tree (glade) tool active: no labels at all; any other tool with Temp copies of runs, i.e. an
edit of an existing run: just that run's label is skipped). The game has no general "show names" setting for areas; `hideOverlay` is
the one switch it honors.

## The cache

Rebuilt only when the catalog moved (`SkiRunCatalogSystem.Revision`, or `Rebuilds`: a pure move of a run keeps the Revision hash, the
rows' centers are refreshed by every catalog rebuild) AND the labels are about to be drawn. A rebuild reads the rows (entity, name,
band, center, outline), samples the terrain once per label, and rebuilds a label's text mesh only when its (clean text, band)
changed. The fade is vertex alpha: rewritten for every label only when the quantized step moves.

## Phase and cost

Phase: Rendering, but NOT anchored to `OverlayRenderSystem` (nothing is written to its buffers: this is `Graphics.DrawMesh`).

Cost: option off / out of zoom range / hidden = a bool read, a zoom read and a return. Drawn and unchanged = one DrawMesh per label
within the cull radius (`RunLabelRules.CullRadius`), a few dozen on a big resort (UNVERIFIED, expected well under 0.1 ms); a rebuild
= one pass over the rows + one TMP text mesh per changed label (UNVERIFIED, expected ~0.05 ms per label).

## Logs

`SkiRunLabelSystem created`; ONE INFO line per load (ARMED); one WARN if the overlay text material is missing; a one-shot `!!!` that
switches the labels off until the next load. Nothing per frame, per rebuild or per label.
