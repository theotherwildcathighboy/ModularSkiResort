/*
 * src/hub/HubLauncher.tsx - the two hub launchers (OD2: both).
 *
 * Written for this repo (MIT). Created 2026-10-01 (Round U1, implementer U1-A).
 *   - HubLauncherSquare: appended at "UniversalModMenu". Its root element becomes a direct child
 *     of the Universal Mod Panel's button grid, which sizes every child to a
 *     --toolbarToggleButtonSize square, three per row (installed index.css, .button-container_AwB;
 *     the ModdingHook wrapper adds no DOM element - index.js@1239871 + TransitionGroupCoordinator).
 *     That hook renders only while the Universal Mod Panel is open, and the panel's own button
 *     on the right menu appears only once some mod appended to it (research 2.E.1).
 *   - HubFloatingButton: appended at "GameTopLeft" (mouse/keyboard layout only; the gamepad layout
 *     does not render that hook, research 2.E.1), hidden while the setting ShowHubButton is off.
 * Both toggle the hub (src/hub/hubState.ts: C# when connected, else the local "not connected"
 * notice) and show `selected` while it is open. They subscribe hubOpen$ / showHubButton$ and
 * local values only (the "connected" flag is relayed by HubRoot; amended by U1-C 2026-10-01).
 * Tooltip (amended by U1-C 2026-10-01): an explicit cs2/ui Tooltip. The Button's `tooltipLabel`
 * prop is an OVERFLOW tooltip: the base button wraps itself in a Tooltip only while its first
 * child's scrollWidth > clientWidth (index.js@629394, `E&&c&&(D=Tooltip...)`), so on an icon
 * button it never showed. Tooltip passes its ref to the Button through the DOM-node context
 * (index.js@529099, @528630) and, by code reading, adds no DOM element of its own (the balloon is
 * portalled, index.js@540726), so the Universal Mod Panel grid still sees the button itself as
 * its child [L: field test 3].
 * Icon: the Ski Resort toolbar tab's own icon (SkiResortTab.png, assetdb://global/aed39078...,
 * gamedata-mirror/ImportedData/SkiResortCategory/SkiResortCategory.Prefab:18), so the launcher
 * and the vanilla tab look alike. That an assetdb:// URL loads in a mod's <img> is [L]: the
 * vanilla toolbar renders the same string through the same Gameface. tinted={false} keeps its
 * colours (cs2/ui FloatingButton defaults to a tinted mask, index.js@713480).
 */
import { useValue } from "cs2/api";
import { Button, FloatingButton, Tooltip } from "cs2/ui";
import { showHubButton$ } from "../bindings";
import { LOC, useT } from "../l10n";
import { toggleHub, useHubConnected, useHubOpen } from "./hubState";

export const HUB_ICON = "assetdb://global/aed3907873aa4d74bb420190d3044e78";

export function HubLauncherSquare(): JSX.Element {
  const t = useT();
  const connected = useHubConnected();
  const open = useHubOpen(connected);
  return (
    <Tooltip tooltip={t(LOC.launcherTooltip)}>
      <Button
        variant="floating"
        src={HUB_ICON}
        tinted={false}
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
        src={HUB_ICON}
        tinted={false}
        selected={open}
        onSelect={() => toggleHub(connected)}
      />
    </Tooltip>
  );
}
