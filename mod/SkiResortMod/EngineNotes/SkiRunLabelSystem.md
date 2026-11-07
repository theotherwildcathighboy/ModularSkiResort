# SkiRunLabelSystem: how run names are drawn flat on the ground, reusing the district-label machinery (Game.dll 1.6.0f1 baseline)

The system writes the name of each of our runs flat on the ground at the run's center. The name uses the piste-map color of its difficulty. It shows at the
zoom range where the piste map reads. The names fade in and out like the district names. The default is on (option `ShowRunNamesOnTheHill`).

## Which mechanism

Vanilla's district / street names are NOT one reusable "label" service:

* Streets (`Game.Rendering/AggregateMeshSystem`, `NetLabel` prefab) and districts (`AreaBufferSystem` name mesh, `DistrictPrefab`)
  each build a per-entity `LabelVertex` buffer (a TextMeshPro text mesh, `AreaBufferSystem.UpdateLabelVertices` :1010-1196). They draw it
  with a per-area-type NAME material (`AreaTypePrefab.m_NameMaterial`, set up :816-821, drawn `AreaRenderSystem` :81-100).
* The area path cannot be pointed at a run, for these reasons:
  * Runs are Surface areas.
  * The `LabelVertex` / `LabelExtents` buffers exist only in the `DistrictPrefab` archetype (DistrictPrefab.cs:30-31).
  * `UpdateLabelVertices` walks EVERY area of the type and indexes the buffer accessor (:1033-1036). This would need the buffer on every
    building's grass sub-area.
  * The color is one `AreaNameData` per PREFAB (:481-492), not per run.
  * The name material belongs to the vanilla Surface `AreaTypePrefab`.
* `OverlayRenderSystem.Buffer.DrawText(entity, pos, cameraFace)` (:189-198) is the public text path (`GuideLinesSystem` uses it for
  water-source names). It has no color, no scale (the matrix is TRS(pos, identity, 1), :193) and no ground orientation.

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
  (`RunRow.CenterX` / `CenterZ`). y is the terrain height + `LiftM`.

What is ours: the per-run mesh (color and fade in the vertex colors), the fade rule and the draw (`Graphics.DrawMesh`, the camera of the
game). The fade rule is `RunLabelRules.FadeAlpha`. The vanilla district fade is inside its shader, which the decompile does not show.

## Gates (cheapest first)

* Option `ShowRunNamesOnTheHill`.
* Game mode armed.
* `RenderingSystem.hideOverlay`. Photo mode / screenshot hides the district names the same way (`AreaRenderSystem` :81).
* The fade: with zoom outside the range, the system draws nothing and rebuilds nothing.
* A tool working on runs:
  * If the area tool holds one of our run prefabs, or the tree (glade) tool is active, no labels draw at all.
  * If any other tool has Temp copies of runs (i.e. an edit of an existing run), only that run's label is skipped.

The game has no general "show names" setting for areas. `hideOverlay` is the one switch it honors.

## The cache

The system rebuilds the cache only when both of these conditions hold:

* The catalog moved (`SkiRunCatalogSystem.Revision`, or `Rebuilds`). `Rebuilds` is there because a pure move of a run keeps the Revision
  hash, and every catalog rebuild refreshes the rows' centers.
* The labels are about to be drawn.

A rebuild reads the rows (entity, name, band, center, outline), samples the terrain once per label, and rebuilds a label's text mesh only
when its (clean text, band) changed. The fade is vertex alpha. The system rewrites it for every label only when the quantized step moves.

## Phase and cost

Phase: Rendering, but NOT anchored to `OverlayRenderSystem` (nothing is written to its buffers: this is `Graphics.DrawMesh`).

Cost:

* Option off / out of zoom range / hidden = a bool read, a zoom read and a return.
* Drawn and unchanged = one DrawMesh per label within the cull radius (`RunLabelRules.CullRadius`), a few dozen on a big resort
  (UNVERIFIED, expected well under 0.1 ms).
* A rebuild = one pass over the rows + one TMP text mesh per changed label (UNVERIFIED, expected ~0.05 ms per label).

## Logs

* `SkiRunLabelSystem created`
* ONE INFO line per load (ARMED)
* one WARN if the overlay text material is missing
* a one-shot `!!!` that switches the labels off until the next load

Nothing logs per frame, per rebuild or per label.
