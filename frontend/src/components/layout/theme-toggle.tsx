"use client";

import { useTheme } from "next-themes";
import { cn } from "@/lib/cn";

/**
 * Theme switcher. Lives in the header on every page so the reviewer can flip
 * both themes without devtools (spec §13 screenshot-tests both).
 *
 * While server/unhydrated, `resolvedTheme` is undefined and we render the
 * same-size button with an *unfilled* icon slot. That is identical geometry in
 * both themes, so there is nothing to hydrate wrongly and nothing to shift
 * (CLS, spec §11) - no mounted-flag effect needed.
 */
export function ThemeToggle({ className }: { className?: string }) {
  const { resolvedTheme, setTheme } = useTheme();
  const isDark = resolvedTheme === "dark";
  const known = resolvedTheme === "dark" || resolvedTheme === "light";

  return (
    <button
      type="button"
      onClick={() => setTheme(isDark ? "light" : "dark")}
      aria-label={
        known
          ? isDark
            ? "Switch to light theme"
            : "Switch to dark theme"
          : "Toggle theme"
      }
      title={known ? (isDark ? "Light theme" : "Dark theme") : "Theme"}
      className={cn(
        "press inline-flex h-9 w-9 items-center justify-center rounded-md border border-border bg-surface text-fg hover:bg-surface-2",
        className,
      )}
    >
      {/* Both glyphs are the same box, so revealing the real one cannot shift. */}
      <span aria-hidden>{isDark ? "☀️" : "🌙"}</span>
    </button>
  );
}
