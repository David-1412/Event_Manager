import { describe, expect, it, vi, afterEach } from "vitest";
import { act, renderHook } from "@testing-library/react";
import { LG_MIN_WIDTH_QUERY, useMediaQuery } from "@/lib/use-media-query";

/**
 * `useMediaQuery` decides a behaviour, not just a style: on the browse page it is
 * what separates "tap opens the event" from "tap previews it". jsdom does not
 * implement matchMedia at all, so the no-matchMedia path is not a hypothetical -
 * it is the one every component test in this repo runs through, and it has to be
 * the wide-screen answer for those tests to mean anything.
 */

type Listener = (event: MediaQueryListEvent) => void;

/** Minimal MediaQueryList standing in for the browser one jsdom is missing. */
function fakeMql(initial: boolean) {
  let matches = initial;
  const listeners = new Set<Listener>();
  return {
    get matches() {
      return matches;
    },
    addEventListener: vi.fn((_type: string, fn: Listener) => listeners.add(fn)),
    removeEventListener: vi.fn((_type: string, fn: Listener) => listeners.delete(fn)),
    emit(next: boolean) {
      matches = next;
      for (const fn of listeners) fn({ matches: next } as MediaQueryListEvent);
    },
    listeners,
  };
}

function withMatchMedia(mql: ReturnType<typeof fakeMql> | null) {
  const w = window as unknown as { matchMedia?: unknown };
  const original = w.matchMedia;
  if (mql === null) delete w.matchMedia;
  else w.matchMedia = vi.fn(() => mql);
  return () => {
    if (original === undefined) delete w.matchMedia;
    else w.matchMedia = original;
  };
}

describe("useMediaQuery", () => {
  afterEach(() => {
    delete (window as unknown as { matchMedia?: unknown }).matchMedia;
  });

  it("reports a matching query", () => {
    const restore = withMatchMedia(fakeMql(true));
    const { result } = renderHook(() => useMediaQuery(LG_MIN_WIDTH_QUERY));
    expect(result.current).toBe(true);
    restore();
  });

  it("reports a non-matching query", () => {
    const restore = withMatchMedia(fakeMql(false));
    const { result } = renderHook(() => useMediaQuery(LG_MIN_WIDTH_QUERY));
    expect(result.current).toBe(false);
    restore();
  });

  it("follows the query as the viewport changes", () => {
    const mql = fakeMql(false);
    const restore = withMatchMedia(mql);
    const { result } = renderHook(() => useMediaQuery(LG_MIN_WIDTH_QUERY));
    expect(result.current).toBe(false);
    act(() => mql.emit(true));
    expect(result.current).toBe(true);
    restore();
  });

  it("subscribes to and unsubscribes from the query", () => {
    const mql = fakeMql(true);
    const restore = withMatchMedia(mql);
    const { unmount } = renderHook(() => useMediaQuery(LG_MIN_WIDTH_QUERY));
    expect(mql.addEventListener).toHaveBeenCalledWith("change", expect.any(Function));
    unmount();
    expect(mql.removeEventListener).toHaveBeenCalledWith("change", expect.any(Function));
    expect(mql.listeners.size).toBe(0);
    restore();
  });

  it("treats a missing matchMedia as not matching, without throwing", () => {
    // The jsdom path, and any non-browser render. "Not matching" is the wide-screen
    // answer, which is the desktop default the browse layout is built around.
    const restore = withMatchMedia(null);
    expect(() => {
      const { result } = renderHook(() => useMediaQuery(LG_MIN_WIDTH_QUERY));
      expect(result.current).toBe(false);
    }).not.toThrow();
    restore();
  });

  it("follows the new query after re-subscribing", () => {
    const a = fakeMql(true);
    const b = fakeMql(false);
    const w = window as unknown as { matchMedia: unknown };
    const original = w.matchMedia;
    w.matchMedia = vi.fn((q: string) => (q === LG_MIN_WIDTH_QUERY ? a : b));
    const { result, rerender } = renderHook(
      ({ query }: { query: string }) => useMediaQuery(query),
      { initialProps: { query: LG_MIN_WIDTH_QUERY } },
    );
    expect(result.current).toBe(true);
    rerender({ query: "(min-width: 48rem)" });

    // Unsubscribed from the old query and subscribed to the new one.
    expect(a.listeners.size).toBe(0);
    expect(b.listeners.size).toBe(1);
    // The value still holds until the new query reports: useState's initializer
    // runs once, so the switch arrives as a change event rather than at rerender.
    expect(result.current).toBe(true);
    act(() => b.emit(false));
    expect(result.current).toBe(false);

    w.matchMedia = original;
  });

  it("names Tailwind's lg breakpoint", () => {
    expect(LG_MIN_WIDTH_QUERY).toBe("(min-width: 64rem)");
  });
});
