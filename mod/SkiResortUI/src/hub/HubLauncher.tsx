/*
* src/hub/HubLauncher.tsx - the two hub launchers.
*
*   - HubLauncherSquare: appended at "UniversalModMenu". Its root element becomes a direct child of the Universal Mod Panel's
*     button grid, which sizes every child to a --toolbarToggleButtonSize square, three per row (installed index.css,
*     .button-container_AwB; the ModdingHook wrapper adds no DOM element - index.js@1239871 + TransitionGroupCoordinator).
*     That hook renders only while the panel is open.
*   - HubFloatingButton: appended at "GameTopLeft" (mouse/keyboard layout only; the gamepad layout does not render that
*     hook), hidden while the setting ShowHubButton is off.
* Both toggle the hub (src/hub/hubState.ts) and show `selected` while it is open. They subscribe to hubOpen$ / showHubButton$
* and local values only; the "connected" flag is relayed by HubRoot.
* Tooltip: an explicit cs2/ui Tooltip. The Button's `tooltipLabel` prop is an overflow tooltip: the base button wraps itself
* in a Tooltip only while its first child's scrollWidth > clientWidth (index.js@629394), so it never showed on an icon button.
* Tooltip passes its ref to the Button through the DOM-node context (index.js@529099, @528630) and adds no DOM element of its
* own (the balloon is portalled, index.js@540726), so the panel grid still sees the button itself as its child.
* Icon: SkiLauncherGlyph.svg, a white one-color glyph bundled by webpack into SkiResortMod-img/ and drawn as a tinted mask on
* the accent plate like every other button in the top-left row (TintedIcon: style.maskImage = url(src), index.js@633525;
* .tinted-icon_iKo fills it with --iconColor, index.css@28104 / @75970). FloatingButton tints by default (index.js@713480);
* Button does not (@713162), so both pass `tinted`. A mask that fails to load leaves an empty plate (no onError, unlike <img>).
* SkiResortTab.png (HUB_ICON) is drawn nowhere and stays as a rollback: src={HUB_ICON} tinted={false} on both.
* The glyph is a byte copy of docs/research/art/icons-2026-10-05/launcher-glyph/launcher-c.svg, made by
* assets/blender/ski_launcher_glyph.py.
*/
import { useValue } from "cs2/api";
import { Button, FloatingButton, Tooltip } from "cs2/ui";
import { showHubButton$ } from "../bindings";
import { LOC, useT } from "../l10n";
import { toggleHub, useHubConnected, useHubOpen } from "./hubState";
import launcherGlyph from "./SkiLauncherGlyph.svg";

/** coui://ui-mods/SkiResortMod-img/SkiLauncherGlyph.svg (webpack asset/resource). */
export const HUB_GLYPH: string = launcherGlyph;
/** Rollback only: the untinted picture used before the glyph. */
export const HUB_ICON = "assetdb://global/aed3907873aa4d74bb420190d3044e78";

export function HubLauncherSquare(): JSX.Element {
  const t = useT();
  const connected = useHubConnected();
  const open = useHubOpen(connected);
  return (
    <Tooltip tooltip={t(LOC.launcherTooltip)}>
      <Button
        variant="floating"
        src={HUB_GLYPH}
        tinted
        selected={open}
        onSelect={() => toggleHub(connected)}
      />
    </Tooltip>
  );
}

export function HubFloatingButton(): JSX.Element | null {
  const t = useT();
  const show = useValue(showHubButton$);
  const connected = useHubConnected();
  const open = useHubOpen(connected);
  if (!show) return null;
  return (
    <Tooltip tooltip={t(LOC.launcherTooltip)}>
      <FloatingButton
        src={HUB_GLYPH}
        tinted
        selected={open}
        onSelect={() => toggleHub(connected)}
      />
    </Tooltip>
  );
}
