/*
 * src/hub/HubLauncher.tsx - the two hub launchers. HubLauncherSquare is appended at "UniversalModMenu". Its root becomes a direct child
 * of the Universal Mod Panel's button grid, so it must stay one square element. HubFloatingButton is appended at "GameTopLeft" (mouse/keyboard
 * layout only), and it is hidden while ShowHubButton is off. Both toggle the hub (hubState.ts) and show `selected` while it is open.
 * The tooltip is an explicit cs2/ui Tooltip (a Button's `tooltipLabel` never shows on an icon button). The icon is a tinted mask
 * (`tinted` on both: Button does not tint by default).
 * Engine notes: EngineNotes/HubLauncher.tsx.md
 */
import { useValue } from "cs2/api";
import { Button, FloatingButton, Tooltip } from "cs2/ui";
import { FOCUS_DISABLED } from "cs2/input";
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
        focusKey={FOCUS_DISABLED}
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
        focusKey={FOCUS_DISABLED}
        src={HUB_GLYPH}
        tinted
        selected={open}
        onSelect={() => toggleHub(connected)}
      />
    </Tooltip>
  );
}
