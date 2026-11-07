# BuildStrip.tsx: the Build strip as the Panel footer, its fixed layout, locked tiles and the focus-key rule

The Build strip is the footer of the hub panel. It has three tabs (Lifts | Runs | Buildings), a fixed-height family row, the icon tiles of the
shown tab, two fixed one-string status lines and the one dot.

## Why a footer

`cs2/ui` Panel renders `footer` in its own div outside the content Scrollable (`index.js@684600`, `@733600`). So the strip never
scrolls away. Its tiles never move under the mouse.

## Tabs and tiles

- The last tab used is the local value `hubState.buildTab$` (default: the tab the idle NEXT line points at). The tab follows the active
  tool. A tab shows only its own groups:
  - Lifts: stations + lines.
  - Runs: the four runs.
  - Buildings: the sign, every sent lodge tier as its own tile, the school and the patrol huts.

  Tabs only filter what is drawn, so every launch id, binding and trigger is unchanged. The one dot also shows on the tab that holds its
  tile.
- The family row under the tabs holds:
  - Lifts: the lift-type chips and the LIFT_TOOL toggle (`hubTriggers.liftLineToggle` -> `LiftLineToolSystem.TryActivate("hub")`).
  - Runs: the tree tool. Since 2026-10-24 there is no grading chip: the Cat track tile opens `CatTrackGradeToolSystem` through
    ApplyLaunch, contract 16.
  - Buildings: the group captions.

  The row is always drawn at one fixed height, so a tab switch never moves the tabs under the mouse. The toggles are ok-panel only
  (`FooterInfo.liftTool`). On a contract mismatch the strip reads core only.
- Tiles: ONE nowrap row. The tiles use `flex: 1 1 0; min-width: 0`. The flex-grow of each group = its drawn tile count, so all tiles are
  equal at any panel width and text scale (no text in tiles). A group none of whose items C# sent is not drawn. The lift types are
  alternatives (`hubRules.pickerSets`): the strip draws the three tiles of the chosen type. The choice is session state (`pick$`, a
  module-level local value, reset by a UI reload).
- Locked tiles are dimmed with a Lock glyph, not `disabled` (whether a disabled `<button>` gets mouse events in Gameface is unverified).
  A click shows the reason in line 2 and still sends `launch(id)`. C# refuses and logs "refused - locked" (`SkiResortHubUISystem`
  `ApplyLaunch`), so the click is never silent. The lodge tiers lock for Missing / Locked / NoSign.

## Status lines

The strip has TWO fixed lines (`div.buildStatusLine`, each ONE text node, 1.3 em of XS, nowrap + ellipsis). Each line has a fixed height,
never min-height, or the footer would grow and move the tiles under the mouse. The priority (`hubRules.footerState`) is: empty > hovered
tile > the "Now showing" notice > the active tile > the last locked click > idle (NEXT / NEXT2 by `ResortLive.nextStep`). Line 1 of a
tile is ONE `<LocText>` by `hubRules.line1Variant` on the kind of the item. Line 2: the locked reason, else the hint key from C#, else
the short hint.

"Now showing" notice: `OkFooter` remembers the `follow` value seen at mount. When C# moves it (an automatic area switch), "Now showing
<area>." holds line 1 for `FOLLOW_NOTICE_MS`. A second follow restarts the timer. Unmount clears it. Nothing runs per frame.

`BuildStrip` reads only the core `build` binding (and its own `pick$`), so it is also the footer of the contract-mismatch panel
(`MISMATCH_FOOTER`: no dot, idle "done": an empty footer). Everything else comes in through `info` (`FooterInfo`), built by `OkFooter` (a panel
file: `resorts$`, `selectedResort$`, `resortLive$`, `problems$`, `follow$`).

## Focus: every tile and picker chip is a Button with `focusKey={FOCUS_DISABLED}`

The Panel wraps its footer in a `FocusKeyOverride` whose `KeyFocusController` hosts ONE child (`index.js@684800`). `bp.registerChild`
`@578500` logs "Cannot register second focus key ... KeyFocusControllers can only host a single child". A Button registers itself by
default (`FOCUS_AUTO`, button.tsx `@629600`). `FOCUS_DISABLED` is what the vanilla close button passes (`focusKey:Ug`,
`index.js@686490`). `tools/check-bundle.js` rule R15 fails a focusable `cs2/ui` Button rendered into a Panel header or footer without it.

## Buildings tab rows and the merge toggle (contract 37, 2026-11-06)

The Buildings tab draws two tile rows. Each row has its caption row and is stretched over the strip. The first row holds the sign and the
five lodges. The second row holds the services (`BUILDING_ROW2_KINDS`: bus depot, school, patrol). One row of 12 (with the depot and a merge tile) cut the labels to
"A-f..." in game. On a six-column grid "Bus depot" was still cut, so the services row keeps its own, wider tiles. `harness/layout-check.js`
fails any Buildings tile label cut at text scale 1. Cost: the footer of the Buildings tab is about 117 rem taller than the footers of the other tabs at 1080.
So the content area is that much shorter there, and the tab row sits higher on that tab.

Under the rows, a full-width "Mergeable buildings" toggle fires `armMergeOverlay(!armed)`. The toggle is on the ok panel while the city
has a Welcome Sign (`FooterInfo.mergeTile`). It is not a C# build item and not a tile: it is a mode. Armed, it takes the selected look and carries the green / gray
legend with the outlined counts (`MergedFold.MergeLegend`). Its icon is the game's `Media/Game/Icons/BuildingUpgrade.svg` (rule R10).
Hovered, the status lines name it and say what can be merged. On the Lifts and Runs tabs the status lines carry the legend while armed and
nothing else is hovered or active.
