/*
 * src/hub/HubRoot.tsx - the always-mounted root of the hub (appended at the game's "Game" hook).
 *
 * Written for this repo (MIT). Created 2026-10-01 (Round U1, implementer U1-A).
 * Contract:
 *   - Mounted once per game session view: the "Game" hook renders in exactly one place, inside
 *     the in-game main screen (one `{name:"Game"}` ModdingHook in the installed index.js), so it
 *     exists only in Game mode - never in the main menu or the editor.
 *   - HANDSHAKE (amended by U1-C, 2026-10-01): fires uiHello(HUB_CONTRACT, BUNDLE_STAMP) ONCE per
 *     mount, at the first render where C# is connected (contract$ != CONTRACT_ABSENT); contract
 *     5.3 says "fired once". (U1-A fired it on mount AND again on connect, so C# logged two
 *     "UI hub connected" lines per load.) Before C# registers its bindings there is no trigger
 *     handler either - the two come from the same system - so nothing is lost by waiting. A UI
 *     reload remounts this and says hello again: one line per UI load. C# logs
 *     "UI hub connected: contract N (DLL contract M), stamp ..." with "!!!" on a mismatch.
 *     The panel cannot be the source: it mounts only while open (research 2.A.3 item 13).
 *   - Subscribes only contract$ and hubOpen$ (both change rarely) and relays "connected" to the
 *     launchers (src/hub/hubState.ts useHubConnectedRoot). The panel, and with it every
 *     summary/runs/build/glade subscription, mounts only while the hub is open.
 *   - The panel sits in its own ErrorBoundary, re-created on every open (the boundary unmounts
 *     with the panel), so a crashed panel can be reopened.
 *   - PANEL CRASH (amended 2026-10-01, U1 fixer round, finding tsx-ux-3). A render error above the
 *     four section boundaries (cs2/ui Panel, HubPanel, HubBody) unmounts the panel together with its
 *     X button and its Close/Back (Escape) handler (cs2/ui Panel `ep onAction`, installed
 *     index.js@~684052), while hubOpen stayed true in C#: the notice said "was closed" but nothing
 *     was, and only a launcher click removed it. Now the boundary's onError closes the hub (C#
 *     setHubOpen(false), or the local flag while C# is absent) and sets `crashed`. The notice
 *     (LOC.uiError, plain markup) shows while the crashed panel is still open and after it closed;
 *     it goes away on a click, after CRASH_NOTICE_MS, or when the hub opens again.
 *   - Registrar errors queued at UI boot are sent to C# once it is connected.
 */
import { useCallback, useEffect, useRef, useState } from "react";
import { BUNDLE_STAMP, HUB_CONTRACT, hubTriggers } from "../bindings";
import { LOC, useT } from "../l10n";
import base from "../base.module.scss";
import { ErrorBoundary } from "./ErrorBoundary";
import { HubPanel } from "./HubPanel";
import { closeHub, useHubConnectedRoot, useHubOpen } from "./hubState";
import { flushRegistrarErrors } from "./report";

/** How long the crash notice stays after the crashed panel closed (ms). */
const CRASH_NOTICE_MS = 8000;

export function HubRoot(): JSX.Element | null {
  const t = useT();
  const connected = useHubConnectedRoot();
  const open = useHubOpen(connected);
  const helloSent = useRef(false);
  const [crashed, setCrashed] = useState(false);
  const [prevOpen, setPrevOpen] = useState(open);

  // A fresh open (closed -> open) clears an old crash notice. Done DURING render (React's
  // "previous value in state" pattern), not in an effect: a panel that crashes in its very first
  // render calls onPanelError from the same commit, and a passive effect running after it would
  // clear the new crash flag again.
  if (open !== prevOpen) {
    setPrevOpen(open);
    if (open) setCrashed(false);
  }

  useEffect(() => {
    if (!connected || helloSent.current) return;
    helloSent.current = true;
    hubTriggers.uiHello(HUB_CONTRACT, BUNDLE_STAMP);
    flushRegistrarErrors();
  }, [connected]);

  useEffect(() => {
    if (!crashed || open) return undefined;
    const id = setTimeout(() => setCrashed(false), CRASH_NOTICE_MS);
    return () => clearTimeout(id);
  }, [crashed, open]);

  const onPanelError = useCallback(() => {
    setCrashed(true);
    try {
      closeHub(connected);
    } catch {
      // closeHub's trigger is already guarded (bindings.ts fire); never throw from here
    }
  }, [connected]);

  const dismiss = useCallback(() => {
    setCrashed(false);
    try {
      if (open) closeHub(connected);
    } catch {
      // never throw from an event handler of the crash notice
    }
  }, [open, connected]);

  // Plain markup only: it is also the boundary's fallback, which must not be able to throw.
  const notice = <div className={base.errorNotice} onClick={dismiss}>{t(LOC.uiError)}</div>;

  if (open) {
    return (
      <ErrorBoundary where="panel" fallback={notice} onError={onPanelError}>
        <HubPanel onClose={() => closeHub(connected)} />
      </ErrorBoundary>
    );
  }
  return crashed ? notice : null;
}
