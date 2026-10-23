# HubHeader.tsx: how the hub's title bar sits in the cs2/ui Panel (drag handle, title styles, focus keys)

The header is the panel's title bar: the ski-area selector, or the plain title "Ski Resort" when there is no resort entry.

## Design

- The name is a `cs2/ui` Button styled as the panel's bold text (`.areaName`, no box until hover) between two arrows. A click toggles
  `hubState.areaListOpen$` and the list is `AreaList`, drawn by `HubPanel` as an overlay at the top of the content (position absolute
  inside our body: no Portal, nothing for the title bar to clip, no drag guard needed). The vanilla Dropdown was dropped: its toggle drew
  a light box in the dark title bar with the panel's light text on it.
- Arrows cycle the entries (`src/hub/resortData.ts`), wrapping; with one entry they are inactive (dimmed), never hidden. A click sends
  `selectResort(sign | Null)`; C# answers by pushing `selectedResort` (it may refuse; the UI never assumes).
- The list shows the same entries in the same order (signs in C#'s order, Not linked last and only when it holds anything), each "name"
  + "N runs · N lodges" (`hubRules.resortCountsVariant` / `resortListCounts`). Picking sends `selectResort(entrySign(r))` and closes the
  list; the backdrop closes it. Under the list `RESORT_MENU_NOTE` says what a map selection does (`SkiResortHubUISystem.MapWorldSelection`).
- Rename: pen -> `RenameField` in the selector's place; commit -> `rename(sign, text)` (C# trims, cuts to 64, writes
  `NameSystem.SetCustomName`). Switching resort while typing commits first (the field unmounts).
- Mounted only while the panel is open and the link is OK; it subscribes `resorts$` and `selectedResort$` (on a mismatch only core
  bindings are read and the panel renders `PlainTitle`).

## Where this renders (installed 1.6.2f1 `index.js` / `index.css`)

- `cs2/ui` Panel (`uS`, `index.js@733600`) wraps `header` in `PanelTitleBar` (`IE`, `@686490`): `div.titleBar > [div.iconSpace]
  [div.title > OUR ROOT] [close IconButton]`. A draggable panel clones the whole title bar with a drag `onMouseDown` (`Bj`, `@719900`),
  so every button here stops mousedown like the vanilla close button (`@686490`). The caption and "N / COUNT" do not stop it: the panel
  still drags by them.
- `.title` (`index.css@57050` / `@55900`): flex:1, centered, uppercase, max-height 2.4em with `coh-font-fit-mode: shrink`. Our root
  therefore sets `text-transform: none`, `flex: 1 1 0; min-width: 0` and its own fixed row height (`hub.module.scss` `.switcher`), so the
  fit-shrink has nothing to shrink.
- `Button variant="icon"` + `src` renders the vanilla IconButton (`$b`; variant table `@712900`, IconButton `@633990`), the close
  button's own component; `tinted` draws the glyph as a mask in `--iconColor` (TintedIcon `@633525`).
- Focus: every button passes `focusKey={FOCUS_DISABLED}`, like the close button (`$b` with `focusKey:Ug`, `@686490`). The Panel wraps
  its header in a `FocusKeyOverride` whose `KeyFocusController` hosts one child (`@684800` `Tp`; `registerChild` `@578500`), and a Button
  registers itself by default (`FOCUS_AUTO`, `@629600`): without `FOCUS_DISABLED` the buttons logged "Cannot register second focus key"
  errors to UI.log on every open and close. Mouse clicks are untouched; keyboard / gamepad focus on these buttons is lost.
  `tools/check-bundle.js` rule R15 enforces it.
