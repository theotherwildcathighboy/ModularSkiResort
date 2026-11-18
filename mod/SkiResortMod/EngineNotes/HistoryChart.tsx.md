# HistoryChart.tsx: how the History view draws its charts as inline SVG, and why

The History view's charts are inline SVG built with React only, no chart.js. The harness stubs chart.js, a game patch can change it, and
the vanilla UI draws its small charts as React SVG too.

## Contract

- Pure presentation of `historyRules` buckets. `<BarChart>` draws one slot per bucket. It draws stacked bars with the `up` series above the zero line and the `down` series below it (costs, shown positive in the data). It also draws one optional line (`line`: ski days or the net) through the slot centers. The line is broken where `historyRules.linePoints` has no point. ONE axis (no dual scale): the line shares the bars' scale. Each
  `<BarChart>` is its own chart. The attraction strip is a separate `<LineStrip>` with its own scale.
- Marks: bars are 70 % of a slot (a slot gap of at least 2 rem between bars). A 2 rem surface gap separates stacked segments. The line is 2 rem
  wide. The chart draws partial buckets faded (fill opacity 0.45). It also gives the month in progress a dashed outline.
- SVG elements used: `svg`, `rect`, `path`, `line` (the ones vanilla renders through React). `polyline`, `pattern` and `defs` are never
  used. Coordinates are viewBox units (`VIEW_W` wide), `preserveAspectRatio` "none": the box fills the chart column's width. Text is HTML
  around the SVG (axis labels, the scale), never SVG `<text>`.
- Hover: the plot's own div tracks the pointer (`getBoundingClientRect`). The chart highlights the bucket under the pointer and reports it
  to `onHover` (-1 when the pointer leaves). The parent owns the tooltip.
- Scale gutter: every chart is a row [gutter | plot + axis]. The gutter holds the top value, "0" on the zero line and the bottom value,
  right-aligned, never over a mark. The chart drops a top / bottom value closer than `LABEL_ROOM` to the zero line (the "0" stays). `sizers`
  (every scale label of the view, laid out invisibly) sets the gutter width. So the gutters of the view's charts are equally wide, and their slots line up (the shared hover index).
- Gaps: a slot with `gap` gets a faint band hatched at 45 deg (the dataviz texture channel), no bar. Empty slots without `gap` (before
  the area existed, the rest of this season) stay blank.
- Line ends: lines take `historyRules.linePoints` (null = no point), so they end at the last whole month.
- "This season vs last": `behind` draws last season's same months gray (`HISTORY_COLOR.lastSeason`) as wide bars behind this season's
  narrower colored ones (totals only: one gray bar up, one down). It also draws last season's line in gray.

## Colors

The Finances chart is income (blue) above zero, costs (red) below, the net as a near-white line. It is validated with the dataviz validator
against the panel surface #2a3753 (dark): income / costs and the guests set blue / aqua / yellow pass every check. The net line #f0fbff
and the last-season gray #646c7e are neutrals, not categorical hues (band / chroma "FAIL" by design). Gray vs red CVD dE 8.3, contrast
2.25:1 (WARN: relief = the "Last season" legend label and its number in the tooltip).

## This season so far (T245, 1.0.2)

- `historyRules.seasonSoFar` sums the season in progress from its first month. The month in progress counts for the totals and the best
  month. The change vs last season compares whole months only, with the same calendar months of last season. A last season the area
  entered partway does not compare (no change cell, a "no comparison" line).
- Net shows its change as money, not a percent: a percent of a net that can cross zero misleads.
- Income mix and cost mix are one bar each. `sharesPct` rounds by largest remainder, so the parts add up to 100. The label names the
  biggest part. The tooltip lists every part with its money and percent.
- Snow figures stay on the Snow report card. The block repeats no number another view owns.
- Layout: the block is about 290 rem tall, so it never fits beside the two charts. The pager puts it on page 2 at every height. The charts
  grew into page 1 (H_TWO 110 rem, 28 rem slack at text scale 1).
- Gates: test-history-rules TH11.1-6, test-hub-rules T20.2 (HISTORY key count 63).
