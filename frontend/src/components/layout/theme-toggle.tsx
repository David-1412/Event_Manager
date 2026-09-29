"use client";

import { useTheme } from "next-themes";
import { useSyncExternalStore } from "react";
import { cn } from "@/lib/cn";

/**
 * Theme switcher. Lives in the header on every page so the reviewer can flip
 * both themes without devtools (spec §13 screenshot-tests both).
 *
 * `resolvedTheme` is decided by next-themes' inline script, which the server
 * cannot see - rendering its value directly is what used to blow up hydration
 * (React discarded the whole tree, so the venue pin never mounted). The
 * mounted flag therefore goes through `useSyncExternalStore`: the server
 * snapshot stays `false`, so server markup ("Toggle theme", empty glyph box)
 * renders until hydration completes, then the real theme shows. Same geometry
 * either way, so nothing shifts (spec §11).
 */
const emptySubscribe = () => () => {};
const getServerSnapshot = () => false;
const getClientSnapshot = () => true;

export function ThemeToggle({ className }: { className?: string }) {
  const { resolvedTheme, setTheme } = useTheme();
  const mounted = useSyncExternalStore(emptySubscribe, getClientSnapshot, getServerSnapshot);
  const isDark = mounted && resolvedTheme === "dark";

  return (
    <button
      type="button"
      onClick={() => setTheme(isDark ? "light" : "dark")}
      aria-label={
        !mounted
          ? "Toggle theme"
          : isDark
            ? "Switch to light theme"
            : "Switch to dark theme"
      }
      title={!mounted ? "Theme" : isDark ? "Light theme" : "Dark theme"}
      className={cn(
        "press inline-flex h-9 w-9 items-center justify-center rounded-md border border-border bg-surface text-fg hover:bg-surface-2",
        className,
      )}
    >
      {/* Sun by day, moon by night - same box, so revealing cannot shift. */}
      <span aria-hidden>{isDark ? "\u2600\uFE0F" : "\uD83C\uDF19"}</span>
    </button>
  );
}
