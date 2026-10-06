"use client";

import { useTheme } from "next-themes";
import { useSyncExternalStore } from "react";
import { Card } from "@/components/ui/card";
import { cn } from "@/lib/cn";

const THEMES = [
  { value: "light", label: "Light" },
  { value: "dark", label: "Dark" },
] as const;

// Same hydration guard as ThemeToggle: the resolved theme is unknown on the server.
const emptySubscribe = () => () => {};

/**
 * Theme preference. `next-themes` (the app's theme system, also behind the header
 * toggle) already persists the choice in localStorage, so nothing is stored here.
 */
export function PreferencesCard() {
  const { resolvedTheme, setTheme } = useTheme();
  const mounted = useSyncExternalStore(emptySubscribe, () => true, () => false);

  return (
    <Card className="p-4 sm:p-6">
      <h2 className="text-h3 text-fg">Preferences</h2>
      <div className="mt-4 flex flex-col gap-2">
        <span id="theme-label" className="text-meta font-medium text-fg">
          Theme
        </span>
        <div role="radiogroup" aria-labelledby="theme-label" className="grid grid-cols-2 gap-2 sm:max-w-xs">
          {THEMES.map(({ value, label }) => {
            const selected = mounted && resolvedTheme === value;
            return (
              <button
                key={value}
                type="button"
                role="radio"
                aria-checked={selected}
                onClick={() => setTheme(value)}
                className={cn(
                  "press h-11 rounded-md border text-body font-medium",
                  selected
                    ? "border-brand-600 bg-brand-tint text-brand-600"
                    : "border-border bg-surface text-fg hover:bg-surface-2",
                )}
              >
                {label}
              </button>
            );
          })}
        </div>
      </div>
    </Card>
  );
}
