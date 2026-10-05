/*
 * src/hub/ErrorBoundary.tsx - contains a render error inside our own components.
 *
 * Written for this repo (MIT). Created 2026-10-01 (Round U1, implementer U1-A).
 * Why (research 2.A.1 "No error boundary"): the game renders appended components inside its
 * ModdingHook with NO error boundary around them - the shipped bundle's only componentDidCatch /
 * getDerivedStateFromError are react-dom's own - and in React 18 an error thrown while rendering
 * unmounts the root, which here is the WHOLE game UI [consequence: L, settle with research G.3
 * step 9]. Therefore EVERY component we hand to moduleRegistry.append is wrapped in one of these
 * (src/index.tsx), and the hub panel gets its own (src/hub/HubRoot.tsx).
 *
 * Contract:
 *   - A child render error -> this boundary renders `fallback` (default: nothing) and reports
 *     once through reportUiError (console + C# uiError trigger, deduped per message).
 *   - It cannot catch errors in event handlers, effects' async callbacks or the registrar
 *     (those are caught where they happen).
 *   - The fallback must be trivial markup (no cs2/ui component, no binding), so it cannot throw
 *     itself. Remount the boundary (change its key) to retry; HubRoot does that on every open.
 *   - onError (amended 2026-10-01, U1 fixer round, finding tsx-ux-3): called once per caught
 *     error, after the report, inside its own try/catch, so the owner can react (HubRoot closes the
 *     hub: the panel's X / Escape unmounted with the crashed subtree).
 *   - tools/check-bundle.js's smoke test requires every appended component to return this class
 *     (it recognises static getDerivedStateFromError / prototype.componentDidCatch).
 */
import { Component } from "react";
import type { ErrorInfo, ReactNode } from "react";
import { reportUiError } from "./report";

export interface ErrorBoundaryProps {
  /** Short name of what this boundary guards, used in the log line (e.g. "launcher.menu"). */
  where: string;
  /** Rendered after a failure. Keep it plain markup. */
  fallback?: ReactNode;
  /** Called after a caught render error (exceptions in it are swallowed). */
  onError?: (error: unknown) => void;
  children?: ReactNode;
}

interface ErrorBoundaryState {
  failed: boolean;
}

export class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  constructor(props: ErrorBoundaryProps) {
    super(props);
    this.state = { failed: false };
  }

  static getDerivedStateFromError(): ErrorBoundaryState {
    return { failed: true };
  }

  componentDidCatch(error: unknown, info: ErrorInfo): void {
    reportUiError(this.props.where, error, info?.componentStack ?? undefined);
    try {
      this.props.onError?.(error);
    } catch (e) {
      reportUiError(`${this.props.where}.onError`, e);
    }
  }

  render(): ReactNode {
    if (this.state.failed) return this.props.fallback ?? null;
    return this.props.children ?? null;
  }
}
