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
 * Icon (Round S1 F9, docs/research/ski-days/spec/ch5-launcher.md): SkiLauncherGlyph.svg, a white
 * one-colour glyph bundled by webpack into SkiResortMod-img/ and drawn by the game as a tinted
 * mask on the accent plate, like every other button in the top-left row (TintedIcon:
 * style.maskImage = url(src), installed index.js@633525; .tinted-icon_iKo fills it with
 * --iconColor rgb(250,250,250), index.css@28104 / @75970). FloatingButton tints by default
 * (index.js@713480); Button does not (@713162), so both pass `tinted`. A mask that fails to load
 * leaves an empty plate (no onError, unlike <img>). SkiResortTab.png (HUB_ICON) is no longer
 * drawn and stays only as the rollback: src={HUB_ICON} tinted={false} on both.
 * Glyph (c) "peak and snowflake" ships (the owner's pick, DECISIONS 148): a byte copy of
 * docs/research/art/icons-2026-10-05/launcher-glyph/launcher-c.svg (md5 605f036f...0f78), made by
 * assets/blender/ski_launcher_glyph.py; swapping the pick is a one-file change.
 */
import { useValue } from "cs2/api";
import { Button, FloatingButton, Tooltip } from "cs2/ui";
import { showHubButton$ } from "../bindings";
import { LOC, useT } from "../l10n";
import { toggleHub, useHubConnected, useHubOpen } from "./hubState";
import launcherGlyph from "./SkiLauncherGlyph.svg";

/** coui://ui-mods/SkiResortMod-img/SkiLauncherGlyph.svg (webpack asset/resource; S1 F9). */
export const HUB_GLYPH: string = launcherGlyph;
/** Rollback only (S1 F9): the style-E picture, drawn untinted before S1. */
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
