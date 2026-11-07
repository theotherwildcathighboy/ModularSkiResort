# SkiPisteMapSystem: how the piste-map overlay is cached, drawn and kept job-safe

The overlay draws the run outlines of the shown ski area on the ground, draped on the terrain, in their difficulty colors. Natural runs
are dashed. The run under the mouse in the run list of the hub is drawn twice as wide and fully opaque.

## Reads

- `SkiResortHubUISystem.PisteMapVisible`, `ShownSign` and `HoveredRun`.
- `SkiGladeToolSystem.IsActive` (the tree tool draws its own outlines on the same edges, so the piste map yields).
- `SkiRunCatalogSystem` rows (Gate, Kind, Band; its Revision hash includes the band, so a rating recolors within a frame).
- The `Game.Areas.Node` buffer of each run.

The system makes no ECS write.

## Cache

The system cuts outlines once into a managed stage of straight segments (`PisteMapRules.SolidRing`, natural runs
`PisteMapRules.DashRing`) and copies them into a persistent `NativeArray`. It rebuilds the cache only in these cases:
- The overlay becomes visible.
- The shown area changes.
- The catalog Revision moves.
- Every `BackstopS` while drawn (a pure move of a run keeps the catalog hash).

A build reads the Node buffers with `EntityManager.TryGetBuffer`, which completes the jobs that write Node. This is a sync point on a
build only, never per frame. The cache holds at most `MaxSegments` segments. A run cut short is counted and warned once per load. A
hover never rebuilds the cache: it maps `HoveredRun` to a slot, one job parameter.

## Draw

One `[BurstCompile]` IJob per frame runs over the segment array (the `SkiRunDrawFeedbackSystem.StripDrawJob` precedent). The system
schedules it on the dependencies of `OverlayRenderSystem.GetBuffer` (OverlayRenderSystem.cs:603-634) and registers it with
`AddBufferWriter` (:636-639). `OverlayRenderSystem.OnUpdate` completes every writer, then uploads and clears the lists (:738-741, :781,
:790). Each segment: `Buffer.DrawLine(outline, fill, outlineWidth, StyleFlags.Projected, Line3.Segment, width, roundness (1, 1))`
(:207-211, Projected drapes on the terrain, :99-104). Width = `BaseWidthM` x `PisteMapRules.WidthScale(camera zoom)` (x
`HoverWidthFactor` for the hovered run). Colors are the band colors of the hub (hub.module.scss `.band*`), sRGB / 255:
`OverlayRenderSystem` converts with `Color.linear`. The job reads no ECS data, so `base.Dependency` is neither an input nor assigned.

## Safety (the StripDrawJob rules)

- `m_Segments` is written only in Build, after `CompleteDraw()`.
- Every job depends on the previous overlay writers, ours included. So completing `m_DrawHandle` completes all of ours. `OnGamePreload`,
  `OnGameLoadingComplete`, `OnStopRunning` and `OnDestroy` complete it, and `OnDestroy` disposes `m_Segments` after it.
- `IJobExtensions.EarlyJobInit<PisteDrawJob>()` runs once in `OnCreate` (try/caught), so the first schedule does not pay the
  reflection-data cost. The system reports the first draw of the process apart (JIT) and keeps it out of the draw peak, as RunFeedback
  does.

## Phase

Rendering, `UpdateBefore<SkiPisteMapSystem, Game.Rendering.OverlayRenderSystem>` (Mod.cs, load-bearing: the job must be registered
before `OverlayRenderSystem` completes and clears the lists in the same frame). Rendering runs paused included. The system reads the
state of the hub, which the hub writes at UIUpdate earlier in the same MainLoop frame (SystemOrder.cs:67-68).

## Cost

Hidden = two bool reads and a return. Drawn and unchanged = a few compares, one camera zoom read, GetBuffer + one Schedule +
AddBufferWriter (expected < 0.05 ms, unverified, logged). A change = one cache build (expected 0.05-0.5 ms, logged).

## Logs

- `SkiPisteMapSystem created`.
- The ARMED line once per load.
- The first outline build of a load.
- One WARN per load each for the segment cap, a build over `SlowBuildWarnMs` and a draw over `SlowDrawWarnMs`.
- The first draw of the process.
- `PisteMap [cost]` after `CostLogAfterDraws` draws of a load.
- A one-shot `!!!` that switches the overlay off until the next load (the hub keeps working).

No line per frame, hover, toggle (the hub writes it) or later build (counted, in the dump).

`ToolHidesMap`: the map hides while the glade tool or the Quick runs tool is open, or the cat track tool is in Delete mode. The Quick
runs preview draws runs in the own palette of the map (`PisteFill / PisteOutline / PisteOutlineWidth`), which are internal for that
reason.
