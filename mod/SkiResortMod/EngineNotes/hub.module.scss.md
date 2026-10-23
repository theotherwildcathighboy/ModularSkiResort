# hub.module.scss: the Gameface CSS rules the hub stylesheet follows, and the scrollbar-over-the-tile fix

## Rules for the stylesheet (Gameface 2.2.1.3)

`tools/check-bundle.js` enforces most of these on the compiled CSS.

- Flex only (no grid), no `:not()`, no `space-evenly`, no auto margins.
- LONGHAND properties whenever a value uses `var()`: shorthands with `var()` are unsupported (UI.log "Custom CSS expressions are not
  supported in shorthand declaration"). Outlines are border longhands (`outline` is unprobed in Gameface).
- No flex `gap` / `row-gap` / `column-gap` (the gate fails them): use margins.
- A class whose name contains "blind" declares `height: 0` and `visibility: hidden`.
- Our own classes only (css-loader prefixes them `sr-<name>_<hash>`); vanilla components get our classes through `className` /
  `contentClassName` / `theme` props; no vanilla class map is extended or overridden. Colors and sizes are the game's global CSS
  variables (installed `index.css` `:root`: `--warningColor` #ffa42d `@9388`, `--positiveColor` `@9364`, `--lockedColor` `@8331`,
  `--sectionBackgroundColor` `@8044`, `--selectedColor` `@7300`, `--fontSizeXS/S/M/L` `@5298-5554` ...).
- Our stylesheet is linked after the game's `index.css`, so on equal specificity our rule wins [L: the module loader appends the `<link>`
  at runtime, `index.js@2306554`].
- `display` / `flex-direction` are always written out (Gameface defaults differ from a browser's).
- Sizes that follow the player's text scale are in `em` or `calc(Nrem * var(--fontScale))`.
- Single-line text that must not grow a box: `white-space: nowrap` + `coh-font-fit-mode: shrink` in a bounded box, the pairing vanilla
  uses itself (`index.css@186335` and `@205436`: `white-space:nowrap;coh-font-fit-mode:shrink`). That it shrinks rather than clips in
  game is [likely].
- A VALUE CHANGE NEVER CHANGES LAYOUT: every block above the view and every row has a fixed height in `em` of its own font (or `rem`),
  every number sits in a fixed slot, every sentence is ONE text node with nowrap + ellipsis and its full text in a tooltip; every
  fixed-height text box sets its own `line-height`; no `flex-wrap: wrap`, `position: sticky`, dashed borders, gradients, transforms other
  than the diamond `rotate(45deg)`, fractional `flex-grow` or non-ASCII characters in the stylesheet.

## Near the dim text colors

Dim text colors are LITERAL: `rgba(240, 251, 255, 0.7)` dim and `rgba(240, 251, 255, 0.5)` dimmer. The game's `--textColorDim` /
`--textColorDimmer` are white in `:root` (installed `index.css@2587`, `@3184`; Dimmer is white in the dark-gray-orange style too,
`@12464`; also `@11454`). Precedent for a literal text color: `.listRowUnbound`.

## Near .viewSlot

The `.blind` width samples are height 0 but overflow visible, so their text hangs one line below its cell. Under the LAST row of the
Runs view (`NumCell` blinds) and the Money view (money blinds) the hang ends ~13 rem below the row, 7 rem past the body's 6 rem bottom
padding [cause likely]. The vanilla Scrollable shows its track when `scrollHeight - 1 > offsetHeight` (installed `index.js@638418`,
re-run on any DOM mutation), so a content-sized Scrollable drew a near-full-height bar, and its track (6-10 rem from the right edge, no
gutter) ran over the Money tile, the one row without right-side room.

Fix: the view slot clips what hangs below it (`overflow: hidden`; `flex-shrink: 0` so `overflow: hidden` cannot let it shrink below its
content). The `.blind` rule itself is NOT changed: a clip there could change the measured widths in Gameface (unproven). `.tileRow` also
keeps 10 rem clear on the right, so the fifth tile is clear of a real scrollbar. Measure: `layoutReport.ts` ` over=<n>` (0 = no
overflow). Tooltips are portalled (`index.js@540726`): no clip.
