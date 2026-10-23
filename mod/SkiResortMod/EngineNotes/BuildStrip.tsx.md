# BuildStrip.tsx: the Build strip as the Panel footer, its fixed layout, locked tiles and the focus-key rule

The Build strip is the hub panel's footer: three tabs (Lifts | Runs | Buildings), a fixed-height family row, the icon tiles of the
shown tab, two fixed one-string status lines and the one dot.

## Why a footer

`cs2/ui` Panel renders `footer` in its own div outside the content Scrollable (`index.js@684600`, `@733600`), so the strip never
scrolls away and its tiles never move under the mouse.

## Tabs and tiles

- The last tab used is the local value `hubState.buildTab$` (default: the tab the idle NEXT line points at); the tab follows the active
  tool. A tab shows only its own groups (Lifts: stations + lines; Runs: the four runs; Buildings: the sign, every sent lodge tier as its
  own tile, the school and the patrol huts). Tabs only filter what is drawn, so every launch id, binding and trigger is unchanged; the
  one dot also shows on the tab that holds its tile.
- The family row under the tabs holds the lift-type chips and the LIFT_TOOL toggle (Lifts; `hubTriggers.liftLineToggle` ->
  `LiftLineToolSystem.TryActivate("hub")`), the tree tool and a "Grade cat track" chip (Runs; `hubTriggers.gradeToggle` ->
  `CatTrackGradeToolSystem`), or the group captions (Buildings). It is always drawn at one fixed height so a tab switch never moves the
  tabs under the mouse. The toggles are ok-panel only (`FooterInfo.liftTool`): on a contract mismatch the strip reads core only.
- Tiles: ONE nowrap row; tiles `flex: 1 1 0; min-width: 0`, each group's flex-grow = its drawn tile count, so all tiles are equal at any
  panel width and text scale (no text in tiles). A group none of whose items C# sent is not drawn. The lift types are alternatives
  (`hubRules.pickerSets`): the strip draws the chosen type's three tiles; the choice is session state (`pick$`, a module-level local
  value, reset by a UI reload).
- Locked tiles are dimmed with a Lock glyph, not `disabled` (whether a disabled `<button>` gets mouse events in Gameface is unverified).
  A click shows the reason in line 2 and still sends `launch(id)`: C# refuses and logs "refused - locked" (`SkiResortHubUISystem`
  `ApplyLaunch`), so the click is never silent. The lodge tiers lock for Missing / Locked / NoSign.

## Status lines

TWO fixed lines (`div.buildStatusLine`, each ONE text node, 1.3 em of XS, nowrap + ellipsis; a fixed height, never min-height, or the
footer would grow and move the tiles under the mouse). Priority (`hubRules.footerState`): empty > hovered tile > the "Now showing"
notice > the active tile > the last locked click > idle (NEXT / NEXT2 by `ResortLive.nextStep`). Line 1 of a tile is ONE `<LocText>` by
`hubRules.line1Variant` on the item's kind. Line 2: the locked reason, else C#'s hint key, else the short hint.

"Now showing" notice: `OkFooter` remembers the `follow` value seen at mount; when C# moves it (an automatic area switch), "Now showing
<area>." holds line 1 for `FOLLOW_NOTICE_MS`; a second follow restarts the timer; unmount clears it. Nothing runs per frame.

`BuildStrip` reads only the core `build` binding (and its own `pick$`), so it is also the contract-mismatch panel's footer
(`MISMATCH_FOOTER`: no dot, idle NEXT[Done]). Everything else comes in through `info` (`FooterInfo`), built by `OkFooter` (a panel
file: `resorts$`, `selectedResort$`, `resortLive$`, `problems$`, `follow$`).

## Focus: every tile and picker chip is a Button with `focusKey={FOCUS_DISABLED}`

The Panel wraps its footer in a `FocusKeyOverride` whose `KeyFocusController` hosts ONE child (`index.js@684800`; `bp.registerChild`
`@578500` logs "Cannot register second focus key ... KeyFocusControllers can only host a single child"), and a Button registers itself
by default (`FOCUS_AUTO`, button.tsx `@629600`). `FOCUS_DISABLED` is what the vanilla close button passes (`focusKey:Ug`,
`index.js@686490`). `tools/check-bundle.js` rule R15 fails a focusable `cs2/ui` Button rendered into a Panel header or footer without it.
