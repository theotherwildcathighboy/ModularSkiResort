/*
 * src/hub/hubState.ts - hub open state and the C# connection state, shared by the launchers, HubRoot and the panel.
 * C# owns "is the hub open" (binding hubOpen). While its bindings are absent (contract$ == CONTRACT_ABSENT), a click must still visibly do
 * something (silence must be impossible). So the launchers toggle a LOCAL open flag, and the panel shows "not connected".
 * HubRoot relays "connected" through a local value (linkLocal$), because the launchers live in other React trees.
 * Panel view state is module-level bindLocalValue values that outlive the panel. cs2/ui PanelFoldout is uncontrolled
 * (index.js@726855, @730976), so foldouts read these values once, as initialExpanded.
 * sendHoverRun sends only on a change. resetHoverRun (panel mount) forgets the last value without sending, because C# resets its hover at every load.
 */
import { useEffect } from "react";
import { reportUiError } from "./report";
import { bindLocalValue, useValue } from "cs2/api";
import type { Entity } from "cs2/utils";
import {
  BUNDLE_STAMP, CONTRACT_ABSENT, HUB_CONTRACT, NULL_ENTITY, STANDALONE_STAMP_PREFIX,
  buildStamp$, contract$, hubOpen$, hubTriggers, sameEntity,
} from "../bindings";


/** The views under the tiles */
export type HubView = "overview" | "runs" | "lifts" | "guests" | "money" | "history";

export const HUB_VIEWS: readonly HubView[] = ["overview", "runs", "lifts", "guests", "money", "history"];

/** The selected view. It is "overview" at UI load. It survives closing the hub. A UI reload resets it */
export const view$ = bindLocalValue<HubView>("overview");

/** The Trees foldout of the contract-mismatch panel only. It is expanded by default and remembered */
export const treesOpen$ = bindLocalValue<boolean>(true);

/** The active tool key (hubRules.activeToolKey) the tab follow last saw. It resets when the panel unmounts, so the next open with a tool
 *  active (started while the hub was closed, or kept open meanwhile) selects its view once. */
export const runToolSeen = { value: "" };

/** The Build strip's tab ("lifts" | "runs" | "buildings", hubRules.BuildTab) the player last used. "" = none yet: the strip
 *  then opens on the tab that the idle NEXT line points at. It survives closing the hub. A UI reload resets it. */
export const buildTab$ = bindLocalValue<string>("");

/** The Build strip is open (the player clicked a tab, hubRules.footerOpen). It is closed by default, and closes again when a tool that the
 *  strip started ends, like the game's toolbar (DECISIONS 613). It survives closing the hub. A UI reload resets it. */
export const buildOpen$ = bindLocalValue<boolean>(false);

/** The Buildings tab's family (hubRules.BuildingFamily): the sign and lodges, or the services. */
export const buildFamily$ = bindLocalValue<string>("lodges");

/** The footer's shape ("open|tab|family", set by BuildStrip). When it changes (a tab, a family, the strip opening or closing),
 *  the view pager re-fits at once, not on its next poll. */
export const footerShape$ = bindLocalValue<string>("");

/** The History view's controls (local values, nothing sent). They survive closing the hub. A UI reload resets them. Scope
 *  "area" = the shown ski area, "city" = every live area summed. Range: historyRules.HistoryRange. Both charts and the
 *  attraction strip always draw (hub redesign 2026-11-07). */
export const historyScope$ = bindLocalValue<"area" | "city">("area");
export const historyRange$ = bindLocalValue<string>("months12");

/** "Never a scrollbar" local values (nothing sent to C#):
 *  - folds$: per view, the one open fold (parts.tsx Fold). It works as an accordion: opening one fold closes the other. A view
 *    absent from the map shows FOLD_DEFAULT (all closed). "" = closed by the player. It survives closing the hub. A UI reload resets it.
 *  - areaListOpen$: the header's ski-area list (HubHeader AreaList, drawn over the top of the content, not in a portal).
 *  - problemsOpen$: the problems band's +N popover (StatusBlock). Both close when the panel mounts and on an area switch. */
export const folds$ = bindLocalValue<Readonly<Record<string, string>>>({});
export const FOLD_DEFAULT: Readonly<Record<string, string>> = {};
export const areaListOpen$ = bindLocalValue<boolean>(false);
export const problemsOpen$ = bindLocalValue<boolean>(false);

export function openFold(folds: Readonly<Record<string, string>> | null | undefined, view: string): string {
  const v = folds ? folds[view] : undefined;
  return typeof v === "string" ? v : FOLD_DEFAULT[view] ?? "";
}

export function toggleFold(view: string, id: string): void {
  const cur = folds$.value ?? {};
  const open = openFold(cur, view) === id;
  folds$.update(Object.assign({}, cur, { [view]: open ? "" : id }));
}


let lastHoverSent: Entity = NULL_ENTITY;

/** Send hoverRun only when it differs from the last one sent (no per-move traffic) */
export function sendHoverRun(run: Entity): void {
  if (sameEntity(run, lastHoverSent)) return;
  lastHoverSent = { index: run.index, version: run.version };
  hubTriggers.hoverRun(run);
}

/** Panel mount: forget the last value without sending (C# resets its own at every load) */
export function resetHoverRun(): void {
  lastHoverSent = NULL_ENTITY;
}

export interface FocusedRow {
  kind: "run" | "lift" | "none";
  entity: Entity;
  top: Entity;
}

export const NO_FOCUS: FocusedRow = { kind: "none", entity: NULL_ENTITY, top: NULL_ENTITY };

export const focused$ = bindLocalValue<FocusedRow>(NO_FOCUS);

/** Open flag used only while C# is absent */
const localOpen$ = bindLocalValue<boolean>(false);

/** HubRoot's view of the C# link, relayed to the launchers (local value: no C# observer) */
const linkLocal$ = bindLocalValue<boolean>(false);

/**
 * HubRoot only. It returns true once C# registers the hub bindings (any contract number, matching or not). It subscribes
 * contract$ and relays the result to useHubConnected(). It resets the relayed value on unmount. C# pushes CONTRACT_ABSENT after its
 * one-shot exception, and pushes the contract again at the next load. So "connected" can go true -> false -> true within one mount.
 * On reconnecting, the hook clears the local open flag (used only while absent). Then a "not connected" notice that stayed open
 * does not pop up again at the next disconnect.
 */
export function useHubConnectedRoot(): boolean {
  const connected = useValue(contract$) !== CONTRACT_ABSENT;
  useEffect(() => {
    if (linkLocal$.value !== connected) linkLocal$.update(connected);
    if (connected && localOpen$.value) localOpen$.update(false);
  }, [connected]);
  useEffect(() => () => linkLocal$.update(false), []);
  return connected;
}

/** Launchers: the C# link as relayed by HubRoot (subscribes no C# binding) */
export function useHubConnected(): boolean {
  return useValue(linkLocal$);
}

/** Is the hub panel open: C#'s hubOpen when connected, else the local flag */
export function useHubOpen(connected: boolean): boolean {
  const remote = useValue(hubOpen$);
  const local = useValue(localOpen$);
  return connected ? remote : local;
}

/** Is HubRoot mounted (set by HubRoot's mount effect, cleared on unmount)? */
let rootMounted = false;

export function setRootMounted(mounted: boolean): void {
  rootMounted = mounted;
}

/** Launcher click. Without a mounted HubRoot nothing can open, so the click is reported. toggleHub still fires. */
export function toggleHub(connected: boolean): void {
  if (!rootMounted) {
    reportUiError("launcher", "the hub root (the Game hook's HubRoot) is not mounted, so the panel cannot open",
      connected ? "toggleHub sent" : "C# not connected either");
  }
  if (connected) hubTriggers.toggleHub();
  else localOpen$.update(!localOpen$.value);
}

export function closeHub(connected: boolean): void {
  if (connected) hubTriggers.setHubOpen(false);
  else localOpen$.update(false);
}

export type HubLinkState = "absent" | "mismatch" | "ok";

export interface HubConnection {
  state: HubLinkState;
  dllContract: number;
  /** HUB_CONTRACT, for display */
  uiContract: number;
  dllStamp: string;
  bundleStamp: string;
  stampMatches: boolean;
  standaloneBundle: boolean;
}

export function useHubConnection(): HubConnection {
  const dllContract = useValue(contract$);
  const dllStamp = useValue(buildStamp$);
  const state: HubLinkState =
    dllContract === CONTRACT_ABSENT ? "absent" : dllContract === HUB_CONTRACT ? "ok" : "mismatch";
  return {
    state,
    dllContract,
    uiContract: HUB_CONTRACT,
    dllStamp,
    bundleStamp: BUNDLE_STAMP,
    stampMatches: dllStamp === BUNDLE_STAMP,
    standaloneBundle: BUNDLE_STAMP.startsWith(STANDALONE_STAMP_PREFIX),
  };
}
