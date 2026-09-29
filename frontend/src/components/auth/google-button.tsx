"use client";

import { cn } from "@/lib/cn";

/**
 * Google's own brand mark (a path per quadrant, the four colours are part of the
 * mark and are not re-colourable) with the label Google's branding guidelines
 * ask to be shown next to it, so a third-party sign-in never looks like our own
 * username/password form wearing a costume.
 */
export function GoogleButton({
  onClick,
  loading = false,
  disabled = false,
  fullWidth = true,
  className,
}: {
  onClick: () => void;
  loading?: boolean;
  disabled?: boolean;
  fullWidth?: boolean;
  className?: string;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled || loading}
      aria-busy={loading || undefined}
      className={cn(
        "press relative inline-flex h-11 items-center justify-center gap-3 rounded-md border border-border",
        "bg-surface px-4 text-body font-medium text-fg hover:bg-surface-2",
        "disabled:cursor-not-allowed disabled:bg-surface-2 disabled:text-fg-muted",
        fullWidth && "w-full",
        className,
      )}
    >
      {loading ? (
        <span aria-hidden className="h-4 w-4 animate-spin rounded-full border-2 border-fg-muted border-t-transparent" />
      ) : (
        <svg viewBox="0 0 48 48" width="18" height="18" aria-hidden focusable="false">
          <path
            fill="#4285F4"
            d="M45.1 24.5c0-1.6-.1-2.8-.4-4H24v7.3h11.9c-.2 2-1.6 5-4.5 6.9l-.1.3 6.6 5 .5.1C42.6 36.2 45.1 30.9 45.1 24.5"
          />
          <path
            fill="#34A853"
            d="M24 46c5.9 0 10.9-2 14.5-5.3l-6.9-5.2c-1.8 1.3-4.3 2.2-7.6 2.2-5.8 0-10.7-3.8-12.5-9.1l-.3.1-6.8 5.2-.1.3C7.4 41 15.1 46 24 46"
          />
          <path
            fill="#FBBC05"
            d="M11.5 28.6c-.5-1.4-.7-2.9-.7-4.6s.3-3.2.7-4.6v-.3l-6.9-5.3-.2.1C2.9 16.7 2 20.2 2 24s.9 7.3 2.4 10.1l7.1-5.5"
          />
          <path
            fill="#EA4335"
            d="M24 10.2c4.1 0 6.9 1.8 8.4 3.2l6.1-5.9C34.9 4.1 29.9 2 24 2 15.1 2 7.4 7 4.4 13.9l7.1 5.5C13.3 14 18.2 10.2 24 10.2"
          />
        </svg>
      )}
      {/* Hidden while loading so the button keeps its width, same trick as Button. */}
      <span className={cn(loading && "invisible")}>Continue with Google</span>
      <span className="sr-only">{loading ? "Google sign-in in progress" : ""}</span>
    </button>
  );
}
