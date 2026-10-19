/*
 * src/hub/hubState.ts - hub open state and the C# connection state, shared by the launchers,
 * HubRoot and the panel.
 *
 * Written for this repo (MIT). Created 2026-10-01 (Round U1, implementer U1-A).
 * Contract:
 *   - C# owns "is the hub open" (binding hubOpen; contract 5.3). The launchers send toggleHub,
 *     the panel's X / Escape send setHubOpen(false), and C# opens the hub when a Welcome Sign is
 *     selected (setting OpenHubOnSignSelect).
 *   - While C# has NOT registered the hub bindings (contract$ == CONTRACT_ABSENT: hub system
 *     missing, failed, or the game is between loads) a click must still visibly do something
 *     (silence must be impossible): the launchers then toggle a LOCAL open flag and the panel
 *     shows the "not connected" notice instead of the hub.
 *   - useHubConnection() is for components that mount only while the panel is open: it
 *     subscribes buildStamp$, which nothing else needs.
 *   - Who subscribes what (amended by U1-C, 2026-10-01, to the U1 task rule "launchers subscribed
 *     only to hubOpen and showHubButton"): the always-mounted HubRoot subscribes contract$ (it
 *     needs it for the handshake) and RELAYS "connected" to the launchers through a local value
 *     (linkLocal$, no C# binding). The launchers subscribe hubOpen$ (+ showHubButton$) and local
 *     values only. The launchers live in other append hooks (other React trees), so a context
 *     cannot carry it. If HubRoot never mounts, the launchers see "not connected" and toggle the
 *     local flag; HubRoot's own failure is reported through uiError / the console.
 *   - PANEL VIEW STATE (added 2026-10-03, Round U2, implementer I-3; spec 4.2 "hubState.ts local
 *     values tab$, detailsOpen$, treesOpen$, focused$"): module-level LOCAL values (cs2/api
 *     bindLocalValue: no C# observer, nothing sent). They outlive the panel's mount, so the chosen
 *     tab, the foldouts' state and the focused row survive closing and reopening the hub within one
 *     UI session (a UI reload resets them). cs2/ui PanelFoldout is uncontrolled
 *     (types/ui.d.ts InfoSectionFoldoutProps; index.js@726855 FoldoutItem keeps its own useState
 *     unless `expanded` is passed, which PanelFoldout does not, @730976): the foldouts read
 *     these values ONCE as initialExpanded and write them back from onToggleExpanded.
 *   - CONTRACT 3 (2026-10-04, Round E1, stream S6; econ-round spec ch6 section 6.3): `view$` (the five
 *     views under the tiles, D33) replaces `tab$`; `detailsOpen$` is gone with the Details foldout;
 *     `treesOpen$` now serves only the contract-mismatch panel's TreesFoldout. hoverRun bookkeeping
 *     (D19, design 7.3, deviation U18): `sendHoverRun` is the ONLY caller of hubTriggers.hoverRun and
 *     sends only when the run differs from the last one sent (module level: no per-move traffic; C#
 *     logs only the first valid hover per load); `resetHoverRun` (panel mount) forgets the last value
 *     WITHOUT sending, because C# resets its own hovered run at every load and a stale "last sent"
 *     would swallow the first hover after a load. The panel's unmount, an area switch, a view switch
 *     and leaving the run list send Entity.Null once.
 *   - ROOT MOUNTED (review M12, 2026-10-19; docs/research/code-review-2026-10-19.md): a module-level flag HubRoot sets while it is
 *     mounted. A launcher click while it is false (the "Game" hook moved, another mod's registrar threw before ours, the root
 *     boundary tripped) reports one uiError / console line (reportUiError dedups) and still fires toggleHub, so C# logs the
 *     click: silence is impossible. C# also WARNs once per session when no uiHello came 30 s after its first arm.
 */
import { useEffect } from "react";
import { reportUiError } from "./report";
import { bindLocalValue, useValue } from "cs2/api";
import type { Entity } from "cs2/utils";
import {
  BUNDLE_STAMP, CONTRACT_ABSENT, HUB_CONTRACT, NULL_ENTITY, STANDALONE_STAMP_PREFIX,
  buildStamp$, contract$, hubOpen$, hubTriggers, sameEntity,
} from "../bindings";

// ---- panel view state (local values; contract 3, ch6 section 3) -----------------------------------

/** The views under the tiles (D33): five since contract 3, "history" appended after Finances in contract 10 (T78). */
export type HubView = "overview" | "runs" | "lifts" | "guests" | "money" | "history";

export const HUB_VIEWS: readonly HubView[] = ["overview", "runs", "lifts", "guests", "money", "history"];

/** The selected view: "overview" at UI load; survives closing the hub; a UI reload resets it. */
export const view$ = bindLocalValue<HubView>("overview");

/** The Trees foldout of the CONTRACT-MISMATCH panel only: expanded by default, remembered (OD-15). */
export const treesOpen$ = bindLocalValue<boolean>(true);

/** T31 (2026-10-16): the Build strip's tab ("lifts" | "runs" | "buildings", hubRules.BuildTab) the player last used. "" = none
 *  yet: the strip then opens on the tab the idle NEXT line points at. Local value (nothing sent to C#); survives closing the
 *  hub, a UI reload resets it. */
export const buildTab$ = bindLocalValue<string>("");

/** T78 (contract 10): the History view's controls (local values, nothing sent; they survive closing the hub, a UI reload resets
 *  them). Scope "area" = the shown ski area, "city" = every live area summed; range: historyRules.HistoryRange; charts: guests /
 *  money / both; the attraction strip is off by default (Richard's answer 2, 2026-10-18). */
export const historyScope$ = bindLocalValue<"area" | "city">("area");
export const historyRange$ = bindLocalValue<string>("months12");
export const historyCharts$ = bindLocalValue<string>("both");
export const historyAttraction$ = bindLocalValue<boolean>(false);

// ---- hoverRun (D19 emphasis; ch6 section 3.3) -----------------------------------------------------

/** The last run sent with hoverRun (module level: one panel, one UI). */
let lastHoverSent: Entity = NULL_ENTITY;

/** Send hoverRun only when it differs from the last one sent (design 7.3: no per-move traffic). */
export function sendHoverRun(run: Entity): void {
  if (sameEntity(run, lastHoverSent)) return;
  lastHoverSent = { index: run.index, version: run.version };
  hubTriggers.hoverRun(run);
}

/** Panel mount: forget the last value WITHOUT sending (C# resets its own at every load). */
export function resetHoverRun(): void {
  lastHoverSent = NULL_ENTITY;
}

/** The row last clicked in a list (shown in the focus strip with its pen). */
export interface FocusedRow {
  kind: "run" | "lift" | "none";
  /** RunRow.entity / LiftRow.entity. */
  entity: Entity;
  /** LiftRow.top (Entity.Null for a run). */
  top: Entity;
}

export const NO_FOCUS: FocusedRow = { kind: "none", entity: NULL_ENTITY, top: NULL_ENTITY };

export const focused$ = bindLocalValue<FocusedRow>(NO_FOCUS);

/** Open flag used only while C# is absent. */
const localOpen$ = bindLocalValue<boolean>(false);

/** HubRoot's view of the C# link, relayed to the launchers (local value: no C# observer). */
const linkLocal$ = bindLocalValue<boolean>(false);

/**
 * HubRoot ONLY: true once C# has registered the hub bindings (any contract number, matching or
 * not). Subscribes contract$ and relays the result to useHubConnected(); resets it on unmount.
 * C# pushes CONTRACT_ABSENT after its one-shot exception and the contract again at the next load
 * (U1 fixer round, csharp-3), so "connected" can now go true -> false -> true within one mount:
 * on reconnecting, the local open flag (used only while absent) is cleared, so a "not connected"
 * notice left open does not pop up again at the next disconnect.
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

/** Launchers: the C# link as relayed by HubRoot (subscribes no C# binding). */
export function useHubConnected(): boolean {
  return useValue(linkLocal$);
}

/** Is the hub panel open: C#'s hubOpen when connected, else the local flag. */
export function useHubOpen(connected: boolean): boolean {
  const remote = useValue(hubOpen$);
  const local = useValue(localOpen$);
  return connected ? remote : local;
}

/** Review M12: is HubRoot mounted (set by HubRoot's mount effect, cleared on unmount)? */
let rootMounted = false;

/** HubRoot's mount / unmount (review M12). */
export function setRootMounted(mounted: boolean): void {
  rootMounted = mounted;
}

/** Launcher click. Review M12: without a mounted HubRoot nothing can open, so the click is reported; toggleHub still fires. */
export function toggleHub(connected: boolean): void {
  if (!rootMounted) {
    reportUiError("launcher", "the hub root (the Game hook's HubRoot) is not mounted, so the panel cannot open",
      connected ? "toggleHub sent" : "C# not connected either");
  }
  if (connected) hubTriggers.toggleHub();
  else localOpen$.update(!localOpen$.value);
}

/** Panel X / Escape. */
export function closeHub(connected: boolean): void {
  if (connected) hubTriggers.setHubOpen(false);
  else localOpen$.update(false);
}

export type HubLinkState = "absent" | "mismatch" | "ok";

export interface HubConnection {
  /** absent = C# lacks the bindings; mismatch = C#'s contract != HUB_CONTRACT; ok = render the hub. */
  state: HubLinkState;
  /** C#'s contract number (CONTRACT_ABSENT when absent). */
  dllContract: number;
  /** HUB_CONTRACT, for display. */
  uiContract: number;
  /** The DLL's SkiResortMod.BuildStamp.Value ("" when absent). */
  dllStamp: string;
  /** This bundle's stamp. */
  bundleStamp: string;
  /** dllStamp == bundleStamp: the DLL and this bundle came from the same C# build. */
  stampMatches: boolean;
  /** This bundle came from a UI-only build (npm run build / dev): a stamp difference is expected. */
  standaloneBundle: boolean;
}

/** Connection details for the panel. Subscribes contract$ and buildStamp$. */
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
