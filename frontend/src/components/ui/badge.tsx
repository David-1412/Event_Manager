import type { HTMLAttributes, ReactNode } from "react";
import { cn } from "@/lib/cn";

export type BadgeTone = "neutral" | "brand" | "warn" | "danger" | "info";

const tones: Record<BadgeTone, string> = {
  neutral: "bg-surface-2 text-fg-muted",
  brand: "bg-brand-tint text-brand-600",
  warn: "bg-warn-tint text-warn",
  danger: "bg-danger-tint text-danger",
  info: "bg-info-tint text-info",
};

/**
 * `text-micro` carries size/weight/tracking; uppercase is applied here because
 * Tailwind v4 `@theme` has no text-transform slot (see tokens.css note).
 */
export function Badge({
  tone = "neutral",
  className,
  children,
  ...rest
}: HTMLAttributes<HTMLSpanElement> & { tone?: BadgeTone; children: ReactNode }) {
  return (
    <span
      className={cn(
        "inline-flex items-center gap-1 rounded-sm px-2 py-0.5 text-micro uppercase whitespace-nowrap",
        tones[tone],
        className,
      )}
      {...rest}
    >
      {children}
    </span>
  );
}
