"use client";

import type { ReactNode } from "react";
import { cn } from "@/lib/cn";

/**
 * EmptyState = one sentence + one primary action, no illustration (spec §7).
 * The sentence must echo what the user filtered for, so it never reads as a bug.
 */
export function EmptyState({
  title,
  action,
  className,
}: {
  title: string;
  action?: ReactNode;
  className?: string;
}) {
  return (
    <div className={cn("flex flex-col items-start gap-3 p-4", className)}>
      <p className="text-h3 text-fg">{title}</p>
      {action}
    </div>
  );
}

/**
 * ErrorState = what failed, `Try again`, and the technical detail folded away
 * where it helps a developer without scaring a user (spec §7).
 */
export function ErrorState({
  title = "Couldn’t load events",
  message = "Your filters are kept — just try again.",
  detail,
  onRetry,
  retryLabel = "Try again",
  className,
}: {
  title?: string;
  message?: string;
  detail?: string;
  onRetry?: () => void;
  retryLabel?: string;
  className?: string;
}) {
  return (
    <div
      className={cn("flex flex-col items-start gap-3 rounded-md border border-border bg-surface p-4", className)}
      role="alert"
    >
      <p className="text-h3 text-fg">{title}</p>
      <p className="text-meta text-fg-muted">{message}</p>
      {onRetry && (
        <button
          type="button"
          onClick={onRetry}
          className="press h-9 rounded-md border border-border bg-surface px-4 text-meta font-medium text-fg hover:bg-surface-2"
        >
          {retryLabel}
        </button>
      )}
      {detail && (
        <details className="text-meta">
          <summary className="cursor-pointer text-fg-muted">Details</summary>
          <code className="mt-2 block break-all rounded-sm bg-surface-2 p-2 text-micro text-fg-muted">
            {detail}
          </code>
        </details>
      )}
    </div>
  );
}
