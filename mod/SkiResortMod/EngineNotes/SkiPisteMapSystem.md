# SkiPisteMapSystem: how the piste-map overlay is cached, drawn and kept job-safe

The overlay draws the shown ski area's run outlines on the ground, draped on the terrain, in their difficulty colors; natural runs
are dashed, the run the mouse is over in the hub's run list is drawn twice as wide and fully opaque.

## Reads

`SkiResortHubUISystem.PisteMapVisible`, `ShownSign` and `HoveredRun`; `SkiGladeToolSystem.IsActive` (the tree tool draws its own
outlines on the same edges, so the piste map steps aside); `SkiRunCatalogSystem` rows (Gate, Kind, Band; its Revision hash includes
the band, so a rating recolors within a frame) and each run's `Game.Areas.Node` buffer. No ECS write.

## Cache

Outlines are cut once into a managed stage of straight segments (`PisteMapRules.SolidRing`; natural runs `PisteMapRules.DashRing`)
and copied into a persistent `NativeArray`. Rebuilt only when the overlay becomes visible, the shown area changes, the catalog
Revision moves, or every `BackstopS` while drawn (a pure move of a run keeps the catalog hash). A build reads the Node buffers with
`EntityManager.TryGetBuffer`, which completes the jobs writing Node: a sync point on a build only, never per frame. At most
`MaxSegments` segments; a run cut short is counted and warned once per load. A hover never rebuilds the cache: it maps `HoveredRun`
to a slot, one job parameter.

## Draw

One `[BurstCompile]` IJob per frame over the segment array (the `SkiRunDrawFeedbackSystem.StripDrawJob` precedent), scheduled on
`OverlayRenderSystem.GetBuffer`'s dependencies (OverlayRenderSystem.cs:603-634) and registered with `AddBufferWriter` (:636-639);
`OverlayRenderSystem.OnUpdate` completes every writer, then uploads and clears the lists (:738-741, :781, :790). Each segment:
`Buffer.DrawLine(outline, fill, outlineWidth, StyleFlags.Projected, Line3.Segment, width, roundness (1, 1))` (:207-211; Projected
drapes on the terrain, :99-104). Width = `BaseWidthM` x `PisteMapRules.WidthScale(camera zoom)` (x `HoverWidthFactor` for the hovered
run). Colors are the hub's band colors (hub.module.scss `.band*`), sRGB / 255: `OverlayRenderSystem` converts with `Color.linear`.
The job reads no ECS data, so `base.Dependency` is neither an input nor assigned.

## Safety (the StripDrawJob rules)

- `m_Segments` is written only in Build, after `CompleteDraw()`.
- Every job depends on the previous overlay writers, ours included, so completing `m_DrawHandle` completes all of ours;
  `OnGamePreload`, `OnGameLoadingComplete`, `OnStopRunning` and `OnDestroy` complete it, and `OnDestroy` disposes `m_Segments`
  after it.
- `IJobExtensions.EarlyJobInit<PisteDrawJob>()` runs once in `OnCreate` (try/caught) so the first schedule does not pay the
  reflection-data cost; the first draw of the process is reported apart (JIT) and kept out of the draw peak, as RunFeedback does.

## Phase

Rendering, `UpdateBefore<SkiPisteMapSystem, Game.Rendering.OverlayRenderSystem>` (Mod.cs; load-bearing: the job must be registered
before `OverlayRenderSystem` completes and clears the lists in the same frame). Rendering runs paused included. It reads the hub's
state written at UIUpdate earlier in the same MainLoop frame (SystemOrder.cs:67-68).

## Cost

Hidden = two bool reads and a return; drawn and unchanged = a few compares, one camera zoom read, GetBuffer + one Schedule +
AddBufferWriter (expected < 0.05 ms, unverified, logged); a change = one cache build (expected 0.05-0.5 ms, logged).

## Logs

`SkiPisteMapSystem created`; the ARMED line once per load; the first outline build of a load; one WARN per load each for the segment
cap, a build over `SlowBuildWarnMs` and a draw over `SlowDrawWarnMs`; the first draw of the process; `PisteMap [cost]` after
`CostLogAfterDraws` draws of a load; a one-shot `!!!` that switches the overlay off until the next load (the hub keeps working). No
line per frame, hover, toggle (the hub writes it) or later build (counted, in the dump).

`ToolHidesMap`: the map hides while the glade tool or the Quick runs tool is open, or the cat track tool is in Delete mode (the Quick runs preview draws runs in the map's own
palette, `PisteFill / PisteOutline / PisteOutlineWidth`, which are internal for that reason).
