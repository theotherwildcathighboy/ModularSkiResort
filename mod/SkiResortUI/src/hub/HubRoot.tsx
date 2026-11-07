/*
 * src/hub/HubRoot.tsx - the always-mounted root of the hub (appended at the game's "Game" hook, so it exists only in Game mode).
 * Handshake: HubRoot fires uiHello(HUB_CONTRACT, BUNDLE_STAMP) once per mount, at the first render where C# is connected
 * (contract$ != CONTRACT_ABSENT). The panel cannot send it, because it mounts only while open.
 * HubRoot subscribes only contract$ and hubOpen$ and relays "connected" to the launchers.
 * The panel sits in its own ErrorBoundary, re-created on every open. A render error above the section boundaries unmounts the panel with
 * its X button and Close/Back handler (cs2/ui Panel `ep onAction`, index.js@~684052). Meanwhile hubOpen stays true in C#.
 * So onError closes the hub and shows LOC.uiError until a click, CRASH_NOTICE_MS, or the next open.
 */
import { useCallback, useEffect, useRef, useState } from "react";
import { BUNDLE_STAMP, HUB_CONTRACT, hubTriggers } from "../bindings";
import { LOC, useT } from "../l10n";
import base from "../base.module.scss";
import { ErrorBoundary } from "./ErrorBoundary";
import { HubPanel } from "./HubPanel";
import { closeHub, setRootMounted, useHubConnectedRoot, useHubOpen } from "./hubState";
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

  // A fresh open clears an old crash notice. This happens during render (React's "previous value in state" pattern), not in an
  // effect. A panel that crashes in its first render calls onPanelError in the same commit, and a passive effect would
  // clear the new crash flag again.
  if (open !== prevOpen) {
    setPrevOpen(open);
    if (open) setCrashed(false);
  }

  // The launchers report a click while no HubRoot is mounted (hubState.ts ROOT MOUNTED).
  useEffect(() => {
    setRootMounted(true);
    return () => setRootMounted(false);
  }, []);

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
