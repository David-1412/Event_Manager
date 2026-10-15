"use client";

import { useEffect, useState } from "react";

/**
 * Tailwind's `lg` breakpoint, as a media query. The one place it is written down
 * as a number so JS can ask the same question the CSS is already answering.
 */
export const LG_MIN_WIDTH_QUERY = "(min-width: 64rem)";

/**
 * Whether a CSS media query currently matches.
 *
 * Needed because a *behaviour* on the browse page has to branch on the same width
 * the layout branches on: with the map closed there is no preview panel, so a card
 * tap should open the event - but on a narrow viewport the preview is the bottom
 * sheet, which does exist, so the tap should still preview. Reading the width in JS
 * is the only way to keep the two in step with `lg:` in the CSS; guessing it from a
 * second, hand-maintained threshold is how the two drift apart.
 *
 * `matchMedia` is missing in environments without a real DOM (and in the jsdom this
 * repo's tests run in, which does not implement it), so the absence is treated as
 * "does not match" - which is the wide-screen answer, matching the desktop default.
 */
export function useMediaQuery(query: string): boolean {
  const [matches, setMatches] = useState(() => read(query));
  useEffect(() => {
    if (typeof window.matchMedia !== "function") return;
    const mql = window.matchMedia(query);
    // Subscribe-only: setState in an effect body is a cascading render, and the
    // initial read above already covers mount. The one gap is a resize between
    // render and subscribe, which is a frame wide and self-corrects on the next
    // change event.
    const onChange = (event: MediaQueryListEvent) => setMatches(event.matches);
    mql.addEventListener("change", onChange);
    return () => mql.removeEventListener("change", onChange);
  }, [query]);
  return matches;
}

function read(query: string): boolean {
  if (typeof window === "undefined" || typeof window.matchMedia !== "function") return false;
  return window.matchMedia(query).matches;
}
