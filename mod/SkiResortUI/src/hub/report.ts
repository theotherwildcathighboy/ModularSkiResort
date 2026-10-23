/*
 * Reports UI failures to the browser console and to C# (uiError trigger), once per distinct message per page load,
 * capped at MAX_REPORTED so an error loop cannot flood the mod log. Never throws.
 * Registrar failures can happen at boot before any C# listener exists: they are queued and flushed by HubRoot on mount.
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
