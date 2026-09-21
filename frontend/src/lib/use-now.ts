"use client";

import { useEffect, useState } from "react";

/**
 * `Date.now()` called during render is impure: React Compiler (and its lint
 * rules) rejects it because a re-render would silently recompute it. A ticking
 * clock is genuinely an external system, so it is read from state that an
 * effect advances. Components that need "now" - the JoinButton `starting`
 * threshold, upcoming/past bucketing - take `now` as a prop instead.
 *
 * One second is finer than any copy this app renders and cheap enough to hold.
 */
export function useNow(intervalMs = 1000): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), intervalMs);
    return () => clearInterval(id);
  }, [intervalMs]);
  return now;
}
