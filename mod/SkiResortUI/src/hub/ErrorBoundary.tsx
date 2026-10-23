/*
 * Contains a render error inside our own components. The game renders appended components inside its ModdingHook
 * with no error boundary, and in React 18 a render error unmounts the root, which here is the whole game UI. So
 * every component handed to moduleRegistry.append is wrapped in one of these.
 *
 * - A child render error renders `fallback` (default: nothing) and reports once through reportUiError.
 * - Cannot catch errors in event handlers, async callbacks or the registrar.
 * - The fallback must be trivial markup (no cs2/ui component, no binding) so it cannot throw itself.
 *   Remount (change the key) to retry.
 * - onError runs once per caught error, after the report, in its own try/catch.
 * - tools/check-bundle.js's smoke test requires every appended component to return this class
 *   (it recognizes static getDerivedStateFromError / prototype.componentDidCatch).
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
