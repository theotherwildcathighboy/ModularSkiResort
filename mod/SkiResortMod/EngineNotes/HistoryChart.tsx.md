# HistoryChart.tsx: how the History view draws its charts as inline SVG, and why

The History view's charts are inline SVG built with React only, no chart.js: the harness stubs it, a game patch can change it, and
the vanilla UI draws its small charts as React SVG too.

## Contract

- Pure presentation of `historyRules` buckets. `<BarChart>` draws one slot per bucket: stacked bars with the `up` series above the zero
  line and the `down` series below it (costs, shown positive in the data), and one optional line (`line`: ski days or the net) through the
  slot centers, broken where `historyRules.linePoints` has no point. ONE axis (no dual scale): the line shares the bars' scale. Each
  `<BarChart>` is its own chart; the attraction strip is a separate `<LineStrip>` with its own scale.
- Marks: bars 70 % of a slot (a slot gap of at least 2 rem between bars), a 2 rem surface gap between stacked segments, a line 2 rem
  wide. Partial buckets are drawn faded (fill opacity 0.45), the month in progress also with a dashed outline.
- SVG elements used: `svg`, `rect`, `path`, `line` (the ones vanilla renders through React; `polyline`, `pattern` and `defs` are never
  used). Coordinates are viewBox units (`VIEW_W` wide), `preserveAspectRatio` "none": the box fills the chart column's width. Text is HTML
  around the SVG (axis labels, the scale), never SVG `<text>`.
- Hover: the plot's own div tracks the pointer (`getBoundingClientRect`), the bucket under it is highlighted and reported to `onHover`
  (-1 when the pointer leaves); the parent owns the tooltip.
- Scale gutter: every chart is a row [gutter | plot + axis]. The gutter holds the top value, "0" on the zero line and the bottom value,
  right-aligned, never over a mark; a top / bottom value closer than `LABEL_ROOM` to the zero line is dropped (the "0" stays). Its width
  is set by `sizers` (every scale label of the view, laid out invisibly), so the gutters of the view's charts are equally wide and their
  slots line up (the shared hover index).
- Gaps: a slot with `gap` gets a faint band hatched at 45 deg (the dataviz texture channel), no bar. Empty slots without `gap` (before
  the area existed, the rest of this season) stay blank.
- Line ends: lines take `historyRules.linePoints` (null = no point), so they end at the last whole month.
- "This season vs last": `behind` draws last season's same months gray (`HISTORY_COLOR.lastSeason`) as wide bars behind this season's
  narrower colored ones (totals only: one gray bar up, one down), plus its line in gray.

## Colors

The Finances chart is income (blue) above zero, costs (red) below, the net as a near-white line. Validated with the dataviz validator
against the panel surface #2a3753 (dark): income / costs and the guests set blue / aqua / yellow pass every check. The net line #f0fbff
and the last-season gray #646c7e are neutrals, not categorical hues (band / chroma "FAIL" by design); gray vs red CVD dE 8.3, contrast
2.25:1 (WARN: relief = the "Last season" legend label and its number in the tooltip).
