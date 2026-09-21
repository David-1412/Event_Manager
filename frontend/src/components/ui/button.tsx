"use client";

import { forwardRef, type ButtonHTMLAttributes, type ReactNode } from "react";
import { cn } from "@/lib/cn";

export type ButtonVariant = "primary" | "secondary" | "ghost" | "danger";
export type ButtonSize = "sm" | "md" | "lg";

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
  size?: ButtonSize;
  /** Spinner replaces the label at held width, label stays for a11y (spec §7). */
  loading?: boolean;
  fullWidth?: boolean;
  children: ReactNode;
}

/** px values resolve through `--spacing: 4px`, so p-3 is literally 12px. */
const sizes: Record<ButtonSize, string> = {
  sm: "h-9 px-4 text-meta gap-2",
  md: "h-11 px-5 text-body gap-2",
  lg: "h-12 px-6 text-body gap-2",
};

const variants: Record<ButtonVariant, string> = {
  primary:
    "bg-brand-600 text-brand-fg hover:bg-brand-700 disabled:bg-surface-2 disabled:text-fg-muted",
  secondary:
    "bg-surface text-fg border border-border hover:bg-surface-2 disabled:bg-surface-2 disabled:text-fg-muted",
  ghost:
    "bg-transparent text-brand-600 hover:bg-brand-tint disabled:text-fg-muted disabled:hover:bg-transparent",
  danger:
    "bg-danger text-white hover:brightness-110 disabled:bg-surface-2 disabled:text-fg-muted",
};

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(
  function Button(
    {
      variant = "primary",
      size = "md",
      loading = false,
      fullWidth = false,
      className,
      children,
      disabled,
      type = "button",
      ...rest
    },
    ref,
  ) {
    return (
      <button
        ref={ref}
        type={type}
        disabled={disabled || loading}
        aria-busy={loading || undefined}
        className={cn(
          "press relative inline-flex shrink-0 items-center justify-center rounded-md font-medium",
          "select-none touch-manipulation",
          "disabled:cursor-not-allowed",
          sizes[size],
          variants[variant],
          fullWidth && "w-full",
          className,
        )}
        {...rest}
      >
        {loading && (
          <Spinner aria-hidden className="absolute" size={size === "sm" ? 14 : 16} />
        )}
        {/* invisible placeholder holds the label's width while the spinner shows,
            so a sticky footer never jumps (spec §7) */}
        <span
          className={cn(
            "inline-flex items-center gap-2",
            loading && "invisible"
          )}
        >
          {children}
        </span>
      </button>
    );
  },
);

export function Spinner({
  size = 16,
  className,
  ...rest
}: { size?: number } & React.SVGProps<SVGSVGElement>) {
  return (
    <svg
      viewBox="0 0 24 24"
      width={size}
      height={size}
      fill="none"
      className={cn("animate-spin", className)}
      {...rest}
    >
      <circle cx="12" cy="12" r="9" stroke="currentColor" strokeOpacity="0.25" strokeWidth="3" />
      <path
        d="M21 12a9 9 0 0 0-9-9"
        stroke="currentColor"
        strokeWidth="3"
        strokeLinecap="round"
      />
    </svg>
  );
}
