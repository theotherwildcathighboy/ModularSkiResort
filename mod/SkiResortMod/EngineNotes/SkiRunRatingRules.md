# SkiRunRatingRules: why the run rating cache is keyed on node x/z, how terraform re-snaps are told apart, and the queue and band rules

Pure decisions of the built-run rating cache in `SkiRunCatalogSystem`, linked into tests/ResortBatch so the key, queue and band rules
are tested headless. Nothing here reads the game. Citations are the `Game.dll` 1.6.0f1 decompile baseline.

## Cache key

* `Key` = FNV-1a over every node's x and z float bits, then the node count: the only node values the rating reads.
  `SkiSlopeClassifier.PlanCore` reads a node's y only for its finiteness check (SkiSlopeClassifier.cs:803-805) and rasterizes x and z
  (:835-845); `SkiRunRater` passes the nodes only to `Plan` (SkiRunRater.cs:266) and `ShoelaceArea`. The rated heights come from the
  terrain lattice.
* `YKey` = FNV-1a over every node's y float bits, then the count. It changes when `GroundHeightSystem` re-snaps a terrain-following
  area node (`UpdateHeightsJob` moves a node whose `m_Elevation` is `float.MinValue` by >= 1 cm, GroundHeightSystem.cs:777-791;
  `AreaUtils.AdjustPosition` writes only `m_Position.y`, AreaUtils.cs:672-677), so a terraform under a run moves `YKey` and never
  `Key`. Keying on xyz would flip a run to "pending" on every re-snap of a brush stroke.
* `NeedsRating`: no cache entry gives the caller's missing reason; `Key` differs gives Edit (front: the old rating is no longer the
  run's); `YKey` differs or the entry's terrain epoch is older gives Terrain (background, duty-cycled: the old rating stays visible
  until the re-rate lands).

## Queue

Front reasons are bind and edit (no duty cycle); warm-up, terrain and backstop are background (duty-cycled). A queued run keeps its
place unless the new reason is front and the queued one is not, except that a queued warm-up entry is never promoted by a bind: every
rebuild after the warm-up seed re-asks every unrated row as "bind", which would move the whole remaining warm-up to the front. An edit
still promotes it.

## Row band

-1 = not rated yet (`RateState` 0), -2 = cannot be rated (`RateState` 2 no terrain / 3 too large / 4 failed). Those states are final
for the current outline and terrain, so the hub must not say "Being rated" forever. A band value, not a new `RunRow` field.

## Always-on service

The catalog rates built runs whether the hub is open or not. The pure parts are here: the `RatingServiceState` values (`Service*`),
the background duty factor per panel state, the panel-closed minimum gap between background ratings (`ClosedMinGapS`) and the warm-up
deadline after which the service reports Ready even if warm-up stalled (`WarmupDeadlineS`).
