# The ski area radius: the option, the ring and the site survey

**The rule.** A Welcome Sign owns what lies within the ski area radius of it. A run binds to the nearest sign within the radius of its
polygon center when drawn, and keeps that sign (SkiRunOwnershipSystem). Lifts (base station), lodges, the ski school and patrol huts
resolve to the nearest live sign within the radius at every recompute (SkiResortIndex.TryResolve, SchoolLinkRules.Resolve). The bus
depot is the exception: any distance.

**The number.** `SkiRunOwnershipSystem.TetherMeters` is a static property over the option `SkiResortSettings.SkiAreaRadiusKm`
(float slider 2-10 km, step 0.5, default 5), clamped and stepped by `SkiAreaRadiusRules.ClampKm`. It used to be `const 5000f`. Every
caller is main thread (no Burst job reads it). A change in a running city has these effects:
- Bound runs keep their sign.
- Lifts and buildings move or unlink at the next recompute (16-frame cadence).

No save data holds the radius, so loading a city with another
option value re-links lifts and buildings the same way.

**Player text.** Tooltips built in C# carry the live number (`NO_GATE_IN_RANGE` {RADIUS}, the survey line). Hub texts are number-free
("out of range of every Welcome Sign", "within its radius"). The hub gets no new binding (contract unchanged). The ring and the
Options page show the number.

**The ring** (`SkiAreaRingOverlaySystem`). 160 projected chords per ring (`SkiAreaRadiusRules.RingSegments`, sag under 2 m at 10 km).
A projected overlay covers the whole terrain height range (`OverlayRenderSystem.GetBuffer` passes `heightScaleOffset.y - 50 ..
heightScaleOffset.x + 100`, OverlayRenderSystem.cs:633 in 1.6.2f1). So a chord drapes over a ridge between its ends, and the ring needs
no terrain sampling. Width scales with zoom up to x10 (wider than the piste map's x6, because a whole 10 km ring is only in view far
out). The rings show in these cases:
- Placing a sign: bright ring at the cursor's raycast hit, faint rings at every sign.
- A sign selected (`ToolSystem.selected`): its ring bright.
- Placing a run (area tool, our run prefab).
- The lift tool.
- The object tool with a lodge / school / patrol hut / depot / station prefab (faint rings).

Sign positions come from `SkiRunOwnershipSystem.CollectGatePositions` (managed scan of every building) on activation and every 60
frames. Option `ShowSkiAreaRadius`, default on.

**The parking ring** (second layer of the same system, parking blue, independent of the first). `SkiParkingRules.RadiusM` (600 m)
around a road lodge (`LodgeSupplyRule.IsRoadLodge(row, true)`: A-frame, Chalet, Grand. Never Hut or Summit). Placing one with the
object tool shows two kinds of ring:
- Bright around the ghost (the Temp building of that prefab, its Transform. The cursor's raycast hit when there is no ghost this
  frame).
- Faint around every lodge of the last parking census (`SkiLodgeParkingSystem.CopyLodgePositions`: road lodges with a row).

A selected lodge with a census row: bright around it (its Transform), faint around the others. The Temp lookup is a main-thread
`ToEntityArray` over Temp buildings, only while a road lodge is being placed (a handful of entities). It uses the same option. Nothing
per frame goes to the log. The ARMED line names both layers. Why 600 m: EngineNotes/SkiLodgeParkingSystem.md "The radius".

**The survey:** see EngineNotes/SkiSnowLineOverlaySystem.md, "The cursor lines and the site survey". Tests: ResortBatch AR-1..AR-3.
