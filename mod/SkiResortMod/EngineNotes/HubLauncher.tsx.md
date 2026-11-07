# HubLauncher.tsx: how the two hub launchers attach to the game UI (grid child, tooltip, tinted mask icon)

## The two launchers

- `HubLauncherSquare`: appended at "UniversalModMenu". Its root element becomes a direct child of the Universal Mod Panel's button grid.
  The grid sizes every child to a `--toolbarToggleButtonSize` square, three per row (installed `index.css`, `.button-container_AwB`). The
  ModdingHook wrapper adds no DOM element (`index.js@1239871` + TransitionGroupCoordinator). That hook renders only while the panel is
  open.
- `HubFloatingButton`: appended at "GameTopLeft" (mouse/keyboard layout only. The gamepad layout does not render that hook). It is hidden
  while the setting `ShowHubButton` is off.

Both toggle the hub (`src/hub/hubState.ts`) and show `selected` while it is open. They subscribe to `hubOpen$` / `showHubButton$` and
local values only. `HubRoot` relays the "connected" flag.

## Tooltip

An explicit `cs2/ui` Tooltip. The Button's `tooltipLabel` prop is an overflow tooltip. The base button wraps itself in a Tooltip only
while its first child's `scrollWidth > clientWidth` (`index.js@629394`), so it never showed on an icon button. Tooltip passes its ref to
the Button through the DOM-node context (`index.js@529099`, `@528630`). It adds no DOM element of its own (the balloon is portalled,
`index.js@540726`), so the panel grid still sees the button itself as its child.

## Icon

`SkiLauncherGlyph.svg` is a white one-color glyph bundled by webpack into `SkiResortMod-img/`. It is drawn as a tinted mask on the accent
plate like every other button in the top-left row (TintedIcon: `style.maskImage = url(src)`, `index.js@633525`, and `.tinted-icon_iKo`
fills it with `--iconColor`, `index.css@28104` / `@75970`). `FloatingButton` tints by default (`index.js@713480`). `Button` does not
(`@713162`), so both pass `tinted`. A mask that fails to load leaves an empty plate (no `onError`, unlike `<img>`).

`SkiResortTab.png` (`HUB_ICON`) is drawn nowhere and stays as a rollback: `src={HUB_ICON} tinted={false}` on both. The glyph is made by
`assets/blender/ski_launcher_glyph.py`.
