# HubPanel.tsx: panel frame, link states, and the Panel / Scrollable layout quirks it works around

## Subscriptions and link states

- `HubRoot` mounts the panel only while the hub is open, inside its own `ErrorBoundary`; every `useValue()` therefore unsubscribes when
  the hub closes and C# stops computing panel data. `props.onClose` (the X button and Escape: `cs2/ui` Panel binds it to its close button
  and the Close/Back input action) is routed by `HubRoot` to C# `setHubOpen(false)`.
- Link states, from `useHubConnection` (`hubState.ts`): absent (contract 0) -> the "not connected" notice only (no subscription);
  mismatch (!= `HUB_CONTRACT`) -> plain title, the `CONTRACT_MISMATCH_CORE` line + the Trees foldout, and the Build strip with
  `MISMATCH_FOOTER`; only core bindings are read (build, glade) because C# never gates triggers on the contract, and `LocalizedString`
  fills {UI}/{DLL} in the English fallback even when an old DLL lacks the key (`index.js@435324`); ok -> header = the ski-area switcher,
  content = the start card while the city has no live sign (even when unlinked runs or lifts exist), else the status line + problems band
  (`StatusBlock`), the six tiles (`ViewTiles`) and the selected view, footer = `OkFooter` -> `BuildStrip`. Each part sits in its own
  `ErrorBoundary`: a crash in one leaves the others usable; the view's boundary is keyed by the view, so a crashed view recovers on a
  switch.
- Render cost: `OkPanel` subscribes only the slow bindings (`resorts$`, `selectedResort$`, `runs$`, `lifts$`) and the local `view$`, so a
  snow, guest or money push (`resortLive$`, `runLive$`, `problems$`) never re-renders the frame, the header or the difficulty bar: the
  leaf components that show those numbers subscribe them.
- The body is the anchor (`.bodyAnchor`, position relative) of two overlays drawn over the top of the content: the header's ski-area list
  (`HubHeader` `AreaList`) and the problems band's +N popover (`StatusBlock` `ProblemsPopover`); one dimming backdrop (`OverlayBackdrop`)
  closes either; both close on panel mount and on an area switch. The panel's content must fit without the Scrollable's bar
  (`harness/layout-check.js`).

## Near the layout: fixed panel height

`cs2/ui` Panel renders header, content and footer as three divs, the content div holding the Scrollable (`index.js@684600`; Panel
wrapper `uS` `@733600`), so the footer never scrolls. `DraggablePanel` places itself at top = y * (viewport - panel height) (`Gj`,
`index.js@720659`), so a content-sized panel moves its header, X and footer whenever its content changes height. Fix:
`.panelFixed { height: 72vh; max-height: 780rem }` while a live sign, a run row or a lift row exists; otherwise `max-height: 72vh` (the
start card and the mismatch state are short panels; their one change of height happens at a state change).

## Near the Scrollable gutter

The Scrollable reserves a 16rem gutter when its content overflows (`index.css@29300`
`.scrollable_DXr.track-visible-y_RCA .content_gqa{padding-right: var(--scrollbarPadding)}`, `--scrollbarPadding` = `--scrollbarSize`
16rem `@28600`), which reflows every row the moment the list grows past the panel. `hub.module.scss` sets `--scrollbarPadding: 0rem`
on the Scrollable, as vanilla does on its own (`index.css@133050`, `@255350`), and keeps a constant right padding of its own [likely:
that the Scrollable is the content div's direct child].

## Placement and hover

- Placement: draggable, opening on the right (`initialPosition {0.98, 0.15}`); a dragged position resets when the panel remounts
  (`index.js@720659`). `uiLayout` (`src/hub/layoutReport.ts`): refs on the header, content, footer, the rem probe (out of flow, never
  drawn), the status row, the problems band, the tile row, the view slot and the shown list.
- `hoverRun`: mount forgets the last hover without sending; unmount sends Null; an area switch drops a focused row of the old area and
  sends Null. The view is kept across an area switch.
