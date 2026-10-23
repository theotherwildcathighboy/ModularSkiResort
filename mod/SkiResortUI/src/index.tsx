/*
 * The registrar: the only code the game calls directly. The game calls every mod's registrar in ONE loop with no try/catch
 * (index.js@800454): a throwing registrar skips every later mod. So it NEVER throws (each append and the whole body in try/catch;
 * failures go to the console and are queued for C#), is append-only on documented hooks, and appends PLAIN FUNCTIONS wrapping an
 * ErrorBoundary. The "/*!" banner comment on `hasCSS` is load-bearing (tools/check-bundle.js checks it); never write the at-sign
 * words license/preserve in src comments (terser extracts them too).
 * Engine notes: EngineNotes/index.tsx.md
 */
import type { ModRegistrar } from "cs2/modding";
import "./base.module.scss"; // LOAD-BEARING: keeps SkiResortMod.css in existence (EngineNotes/index.tsx.md)
import { ErrorBoundary } from "./hub/ErrorBoundary";
import { HubFloatingButton, HubLauncherSquare } from "./hub/HubLauncher";
import { HubRoot } from "./hub/HubRoot";
import { queueRegistrarError } from "./hub/report";

/*! SkiResortMod UI module - Modular Ski Resort for Cities: Skylines II (MIT license). */
// Read by the game's module loader: fetch SkiResortMod.css (index.js@2306554).
export const hasCSS = true;

/** "UniversalModMenu": the square in the Universal Mod Panel. */
function SkiHubMenuLauncher(): JSX.Element {
  return (
    <ErrorBoundary where="launcher.menu">
      <HubLauncherSquare />
    </ErrorBoundary>
  );
}

/** "GameTopLeft": the floating button (setting ShowHubButton). */
function SkiHubTopLeftLauncher(): JSX.Element {
  return (
    <ErrorBoundary where="launcher.topLeft">
      <HubFloatingButton />
    </ErrorBoundary>
  );
}

/** "Game": the always-mounted root (handshake + panel). */
function SkiHubRoot(): JSX.Element {
  return (
    <ErrorBoundary where="root">
      <HubRoot />
    </ErrorBoundary>
  );
}

const register: ModRegistrar = (moduleRegistry) => {
  try {
    try {
      moduleRegistry.append("UniversalModMenu", SkiHubMenuLauncher);
    } catch (e) {
      queueRegistrarError("append UniversalModMenu", e);
    }
    try {
      moduleRegistry.append("GameTopLeft", SkiHubTopLeftLauncher);
    } catch (e) {
      queueRegistrarError("append GameTopLeft", e);
    }
    try {
      moduleRegistry.append("Game", SkiHubRoot);
    } catch (e) {
      queueRegistrarError("append Game", e);
    }
  } catch (e) {
    try { console.error("SkiResortMod UI: registrar failed", e); } catch { /* never throw */ }
  }
};

export default register;
