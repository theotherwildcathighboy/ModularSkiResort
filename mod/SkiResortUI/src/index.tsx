/*
 * The registrar: the only code the game calls directly.
 *
 * The "/*!" comment on `hasCSS` below is load-bearing: terser-webpack-plugin writes the manifest banner only when
 * it extracted at least one comment, so it keeps the banner independent of react/jsx-runtime's license comment. It
 * sits on a statement that is always emitted (a comment on the type-only import would be dropped with the import).
 * tools/check-bundle.js checks that it reached SkiResortMod.mjs.LICENSE.txt. Do not write the at-sign words
 * license/preserve in src comments: terser extracts those too.
 *
 * The game (installed 1.6.2f1 Game/UI/index.js) import()s SkiResortMod.mjs, keeps `default` as the registrar and
 * reads `hasCSS` (truthy: loads SkiResortMod.css and waits for it, index.js@2306554). It then calls every mod's
 * registrar in ONE loop with no try/catch (index.js@800454): a throwing registrar skips every later mod at boot and
 * blanks the whole UI on a live reload.
 *
 * Contract:
 *   - default export = a registrar that NEVER throws: each append in its own try/catch, the whole body in another;
 *     failures go to the console and are queued for C# (src/hub/report.ts).
 *   - append-only, on the game's documented hooks (tools/check-bundle.js fails the build otherwise).
 *   - every appended component is a PLAIN FUNCTION wrapping an ErrorBoundary: append(target, c) takes the anchor
 *     form only when `typeof c === "function"`; a React.memo / forwardRef object would be treated as a module path
 *     and throw (index.js@213139).
 *   - `hasCSS = true` is written by hand and base.module.scss is always imported, so SkiResortMod.css always exists.
 *   - append's position argument is ignored for the anchor form (index.js@213139), so none is passed.
 */
import type { ModRegistrar } from "cs2/modding";
import "./base.module.scss"; // LOAD-BEARING: keeps SkiResortMod.css in existence (see header)
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
