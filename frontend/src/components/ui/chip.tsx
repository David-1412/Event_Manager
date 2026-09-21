"use client";

import { forwardRef, type ButtonHTMLAttributes, type HTMLAttributes, type ReactNode } from "react";
import { cn } from "@/lib/cn";

export interface ChipProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  selected?: boolean;
  children: ReactNode;
}

/**
 * 34px tall, radius-full. Selection adds a check glyph *in addition* to the
 * tint so it survives colour-blind simulation (spec §7, principle 7).
 * Uses aria-pressed because a chip toggles rather than navigates.
 */
export const Chip = forwardRef<HTMLButtonElement, ChipProps>(function Chip(
  { selected = false, className, children, ...rest },
  ref,
) {
  return (
    <button
      ref={ref}
      type="button"
      aria-pressed={selected}
      className={cn(
        "press inline-flex h-[34px] shrink-0 items-center gap-2 rounded-full border px-3 text-meta",
        "touch-manipulation",
        selected
          ? "border-brand-600 bg-brand-tint text-brand-600"
          : "border-border bg-surface-2 text-fg hover:border-fg-muted",
        "disabled:cursor-not-allowed disabled:text-fg-muted disabled:opacity-60",
        className,
      )}
      {...rest}
    >
      {selected && (
        <svg
          viewBox="0 0 16 16"
          width="12"
          height="12"
          fill="none"
          aria-hidden="true"
          className="shrink-0"
        >
          <path
            d="M3 8.5 6.2 12 13 4.5"
            stroke="currentColor"
            strokeWidth="2.2"
            strokeLinecap="round"
            strokeLinejoin="round"
          />
        </svg>
      )}
      {children}
    </button>
  );
});

/** Horizontal scroller used by the mobile FilterBar (spec §7). */
export function ChipRow({ className, ...rest }: HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      className={cn(
        "no-scrollbar flex gap-2 overflow-x-auto pb-1 lg:flex-wrap lg:overflow-visible",
        className,
      )}
      {...rest}
    />
  );
}
