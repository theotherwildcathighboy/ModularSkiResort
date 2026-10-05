/*
 * src/hub/report.ts - one place that reports UI failures: browser console + C# (uiError trigger).
 *
 * Written for this repo (MIT). Created 2026-10-01 (Round U1, implementer U1-A).
 * Contract:
 *   - reportUiError(where, error) never throws. It writes console.error with the full error and
 *     fires hubTriggers.uiError(message) ONCE per distinct message per page load (the C# side
 *     also logs each distinct message once). At most MAX_REPORTED distinct messages are sent;
 *     the cap itself is reported once, so a loop of errors cannot flood the mod log.
 *   - Registrar failures happen at UI boot, possibly before any C# listener exists: they are
 *     queued (queueRegistrarError) and sent by HubRoot when it mounts (flushRegistrarErrors).
 *     If the "Game" hook itself failed to append, they reach the console only.
 */
import { hubTriggers } from "../bindings";

const MAX_REPORTED = 20;
const MAX_LENGTH = 400;
const reported = new Set<string>();
let capReported = false;
const pendingRegistrar: string[] = [];

function describe(error: unknown): string {
  if (error instanceof Error) return `${error.name}: ${error.message}`;
  try { return String(error); } catch { return "(unprintable error)"; }
}

/** Report a UI failure once per distinct message. Never throws. */
export function reportUiError(where: string, error: unknown, detail?: string): void {
  try {
    const message = `${where}: ${describe(error)}`.slice(0, MAX_LENGTH);
    console.error(`SkiResortMod UI error in ${where}`, error, detail ?? "");
    if (reported.has(message)) return;
    if (reported.size >= MAX_REPORTED) {
      if (!capReported) {
        capReported = true;
        hubTriggers.uiError(`more than ${MAX_REPORTED} distinct UI errors; further ones go to the browser console only`);
      }
      return;
    }
    reported.add(message);
    hubTriggers.uiError(message);
  } catch {
    // reporting must never become the failure
  }
}

/** Called by the registrar's catch blocks (src/index.tsx). Never throws. */
export function queueRegistrarError(where: string, error: unknown): void {
  try {
    const message = `${where}: ${describe(error)}`.slice(0, MAX_LENGTH);
    console.error(`SkiResortMod UI registrar: ${message}`, error);
    pendingRegistrar.push(message);
  } catch {
    // never throw from the registrar path
  }
}

/** Sends queued registrar errors to C#. HubRoot calls it on mount. */
export function flushRegistrarErrors(): void {
  while (pendingRegistrar.length) {
    const message = pendingRegistrar.shift() as string;
    reportUiError("registrar", message);
  }
}
