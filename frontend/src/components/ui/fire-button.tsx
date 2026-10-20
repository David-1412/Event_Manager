"use client";

import { forwardRef, type ButtonHTMLAttributes, type ReactNode } from "react";
import { Flame } from "lucide-react";
import { Spinner } from "@/components/ui/button";
import { cn } from "@/lib/cn";

export interface FireButtonProps extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, "children"> {
  /** Whether the viewer has already marked / followed. Drives the filled state and
   * `aria-pressed`, so the toggle is announced, not just coloured. */
  active?: boolean;
  /** Text in both states, e.g. "Interested" or "Follow". */
  label: ReactNode;
  /** Text once active, e.g. "Following". Defaults to `label`. */
  activeLabel?: ReactNode;
  /** A count shown after the label - the post's interested total. */
  count?: number;
  loading?: boolean;
  /** `md` for the event page's action card; `sm` (the spec's pill) everywhere else. */
  size?: "sm" | "md";
  fullWidth?: boolean;
}

/**
 * The one "Interested" / "Follow" control: a red pill with a filled flame.
 *
 * Every tracking action in the app uses it - event cards, the event page, forum
 * posts and the profile Follow - so marking interest looks and reads the same
 * wherever it happens.
 *
 * Colours come from the `danger` tokens rather than Tailwind's fixed `red-50` /
 * `red-600`. In light mode they are the same light-red pill with red text; in dark
 * mode the tokens switch to a deep red tint with a lighter red label, where a
 * literal `bg-red-50` would be a glaring white-pink block on a dark card.
 *
 * Inactive is the light pill; active fills solid red so "you already did this" is
 * visible at a glance, and `aria-pressed` carries the same fact to a screen reader.
 */
export const FireButton = forwardRef<HTMLButtonElement, FireButtonProps>(function FireButton(
  {
    active = false,
    label,
    activeLabel,
    count,
    loading = false,
    size = "sm",
    fullWidth = false,
    className,
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
      aria-pressed={active}
      aria-busy={loading || undefined}
      disabled={disabled || loading}
      className={cn(
        "press inline-flex shrink-0 select-none items-center justify-center gap-1.5 rounded-full font-semibold transition-colors",
        "disabled:cursor-not-allowed disabled:opacity-60",
        size === "sm" ? "h-8 px-3 text-xs" : "h-11 px-5 text-meta",
        active
          ? "bg-danger text-white hover:brightness-110"
          : "bg-danger-tint text-danger hover:bg-danger/20",
        fullWidth && "w-full",
        className,
      )}
      {...rest}
    >
      {loading ? (
        <Spinner aria-hidden size={size === "sm" ? 14 : 16} />
      ) : (
        <Flame aria-hidden size={size === "sm" ? 14 : 16} className="fill-current" />
      )}
      <span>{active ? (activeLabel ?? label) : label}</span>
      {count !== undefined && (
        <span className="tabular-nums" aria-label={`${count} interested`}>
          {count.toLocaleString("en-AU")}
        </span>
      )}
    </button>
  );
});
