"use client";

import { ThemeProvider as NextThemesProvider } from "next-themes";
import type { ReactNode } from "react";

/**
 * Class-based dark mode: `tokens.css` defines `.dark` and `@custom-variant dark`
 * points at it. `suppressHydrationWarning` on <html> covers the pre-hydration
 * class swap — that is the only hydration difference we allow.
 */
export function ThemeProvider({ children }: { children: ReactNode }) {
  return (
    <NextThemesProvider
      attribute="class"
      defaultTheme="system"
      enableSystem
      disableTransitionOnChange
    >
      {children}
    </NextThemesProvider>
  );
}
