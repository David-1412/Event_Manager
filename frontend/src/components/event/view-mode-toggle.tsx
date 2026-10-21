"use client";

import { LayoutGrid, List } from "lucide-react";
import type { BrowseLayoutMode } from "@/components/event/browse-layout";
import { cn } from "@/lib/cn";

const OPTIONS: { value: BrowseLayoutMode; label: string; Icon: typeof List }[] = [
  { value: "list", label: "List View", Icon: List },
  { value: "grid", label: "Grid View", Icon: LayoutGrid },
];

/**
 * List View / Grid View, as two pill buttons. A display preference, not a filter:
 * it changes how the same events are laid out, so it sits with the section's tabs
 * rather than with the filters that change which events are shown.
 */
export function ViewModeToggle({
  mode,
  onMode,
  className,
}: {
  mode: BrowseLayoutMode;
  onMode: (mode: BrowseLayoutMode) => void;
  className?: string;
}) {
  return (
    <div
      role="group"
      aria-label="View mode"
      className={cn("inline-flex h-9 shrink-0 items-center rounded-full border border-border bg-surface-2 p-0.5", className)}
    >
      {OPTIONS.map(({ value, label, Icon }) => {
        const active = mode === value;
        return (
          <button
            key={value}
            type="button"
            onClick={() => onMode(value)}
            aria-pressed={active}
            className={cn(
              "press inline-flex h-full items-center gap-1.5 rounded-full px-3 text-meta font-medium transition-colors",
              active ? "bg-surface text-brand-600 shadow-raise" : "text-fg-muted hover:text-fg",
            )}
          >
            <Icon aria-hidden size={15} />
            {label}
          </button>
        );
      })}
    </div>
  );
}
