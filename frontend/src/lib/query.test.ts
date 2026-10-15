import { describe, expect, it, vi, afterEach } from "vitest";
import { renderHook } from "@testing-library/react";
import { createElement, type ReactNode } from "react";
import { SWRConfig } from "swr";
import {
  DEFAULT_QUERY,
  activeFilterCount,
  buildEventsKey,
  followingEventsKey,
  parseQuery,
  queryToSearchParams,
} from "@/lib/query";
import { useFollowingEvents } from "@/features/events/use-events";
import { useAuth } from "@/lib/auth/auth-provider";
import type { EventQuery } from "@/types/events";

vi.mock("@/lib/auth/auth-provider");
const mockedAuth = vi.mocked(useAuth);

/**
 * The activity filter is `?event=` in the address bar and `?sport=` on the wire
 * to the .NET API. These are the two directions of that rename.
 */
function query(overrides: Partial<EventQuery> = {}): EventQuery {
  return { ...DEFAULT_QUERY, ...overrides };
}

describe("tag filter param", () => {
  it("writes ?tag= for the address bar and the API alike", () => {
    expect(queryToSearchParams(query({ tag: "basketball" })).toString()).toBe(
      "tag=basketball",
    );
  });

  it("reads ?tag=", () => {
    expect(parseQuery("?tag=basketball").tag).toBe("basketball");
  });

  it("still reads pre-rename ?event= and ?sport= links", () => {
    expect(parseQuery("?sport=tennis").tag).toBe("tennis");
    expect(parseQuery("?event=tennis").tag).toBe("tennis");
  });

  it("prefers ?tag= when a URL carries several spellings", () => {
    expect(parseQuery("?sport=tennis&event=cricket&tag=netball").tag).toBe("netball");
  });

  it("normalizes a bookmarked ?tag=Tennis to the stored lowercase form", () => {
    expect(parseQuery("?tag=Tennis").tag).toBe("tennis");
  });

  it("accepts any tag rather than a closed vocabulary, but drops a punctuation-only one", () => {
    // "paintball" was rejected when the list was hardcoded; free tags accept it.
    expect(parseQuery("?tag=paintball").tag).toBe("paintball");
    expect(parseQuery("?tag=%23").tag).toBeNull();
  });
});

describe("buildEventsKey", () => {
  it("sends ?tag= to the API endpoint", () => {
    const key = buildEventsKey(query({ tag: "basketball" }), "http://api.test", true);
    expect(key).toBe("http://api.test/api/events?tag=basketball");
  });

  it("keeps ?tag= for a fixture cache identity, which is never fetched", () => {
    const key = buildEventsKey(query({ tag: "basketball" }), "http://api.test", false);
    expect(key).toBe("http://api.test/api/events?tag=basketball");
  });

  it("keeps the remaining filters intact", () => {
    const key = buildEventsKey(
      query({ tag: "soccer", date: "today", radiusKm: 5, sort: "distance", q: "park" }),
      "http://api.test",
      true,
    );
    const url = new URL(key);
    expect(url.searchParams.get("tag")).toBe("soccer");
    expect(url.searchParams.has("sport")).toBe(false);
    expect(url.searchParams.get("q")).toBe("park");
    expect(url.searchParams.get("date")).toBe("today");
    expect(url.searchParams.get("radius")).toBe("5");
    expect(url.searchParams.get("sort")).toBe("distance");
  });

  it("omits the activity filter entirely when unset", () => {
    expect(buildEventsKey(query(), "http://api.test", true)).toBe("http://api.test/api/events");
  });
});

/**
 * `?tab=` selects which endpoint the page queries, not what the query says.
 * That split is the whole design of the Following feed, so both directions of
 * it are pinned: the tab survives a shareable URL and never survives into an
 * API URL, where it would be an unbound parameter on the browse endpoint.
 */
describe("browse tab param", () => {
  it("round-trips ?tab=following", () => {
    expect(parseQuery("?tab=following").tab).toBe("following");
    expect(queryToSearchParams(query({ tab: "following" })).toString()).toBe(
      "tab=following",
    );
  });

  it("is absent from the URL at the default tab, which stays a clean /", () => {
    expect(queryToSearchParams(query()).toString()).toBe("");
    expect(parseQuery("").tab).toBe("all");
  });

  it("falls back to All for a tab it does not know", () => {
    expect(parseQuery("?tab=friends").tab).toBe("all");
  });

  it("is not a filter, so it does not count toward Filters · N", () => {
    expect(activeFilterCount(query({ tab: "following" }))).toBe(0);
  });

  it("never reaches a browse API URL", () => {
    const key = buildEventsKey(query({ tab: "following", tag: "run" }), "http://api.test", true);
    expect(key).toBe("http://api.test/api/events?tag=run");
  });

  it("selects a different endpoint instead", () => {
    const key = followingEventsKey(query({ tab: "following", tag: "run" }), "http://api.test");
    expect(key).toBe("http://api.test/api/events/me/following?tag=run");
  });

  it("addresses the feed by path, never by a viewer id in the query", () => {
    // The server resolves the viewer from the bearer token. A `?viewer=` in the
    // key would be both unbound and an invitation to read someone else's graph.
    const key = followingEventsKey(query({ tab: "following" }), "http://api.test");
    expect(key).toBe("http://api.test/api/events/me/following");
    expect(key).not.toContain("viewer");
  });
});

/**
 * The Following feed's client half.
 *
 * The endpoint is viewer-scoped and answers 401 to an anonymous caller, so the
 * hook's job is to know when not to ask. Both directions are worth pinning: a
 * request fired before the session resolves shows the signed-out user an error
 * where a prompt belongs, and a request fired *for* a signed-in user against the
 * wrong URL would show them the ordinary feed while the tab says Following.
 *
 * These run under fixtures (no NEXT_PUBLIC_API_BASE_URL in the test env), which
 * is also the only way the offline mirror's follow graph gets asserted: the set
 * of hosts it treats as followed is what makes the tab's empty state reachable
 * with no API running.
 */
describe("useFollowingEvents", () => {
  afterEach(() => vi.restoreAllMocks());

  function signedIn(id: string) {
    return {
      user: {
        uid: `firebase-${id}`,
        id,
        displayName: "Test Viewer",
        email: "viewer@example.com",
        photoUrl: null,
        role: "Member",
      },
      loading: false,
      signOut: vi.fn(),
    } as unknown as ReturnType<typeof useAuth>;
  }

  function wrapper({ children }: { children: ReactNode }) {
    return createElement(SWRConfig, { value: { revalidateOnFocus: false } }, children);
  }

  function renderFollowing(search: string, enabled: boolean, auth: ReturnType<typeof useAuth>) {
    mockedAuth.mockReturnValue(auth);
    return renderHook(() => useFollowingEvents(new URLSearchParams(search), enabled), {
      wrapper,
    });
  }

  const anon = {
    user: null,
    loading: false,
    signOut: vi.fn(),
  } as unknown as ReturnType<typeof useAuth>;

  it("asks for nothing while the viewer is anonymous", () => {
    const fetchSpy = vi.spyOn(globalThis, "fetch");
    const { result } = renderFollowing("?tab=following", true, anon);

    expect(fetchSpy).not.toHaveBeenCalled();
    // No error either: the page reads "nothing asked" and shows the sign-in
    // prompt, which is the difference between a prompt and a red box.
    expect(result.current.error).toBeFalsy();
  });

  it("asks for nothing when the tab is not the active one", () => {
    const fetchSpy = vi.spyOn(globalThis, "fetch");
    renderFollowing("", false, signedIn("viewer-1"));

    // The All tab must not pay for the Following feed on every browse, and the
    // Following feed must not be fetched while hidden.
    expect(fetchSpy).not.toHaveBeenCalled();
  });

  it("narrows the offline mirror to followed hosts only", () => {
    const { result } = renderFollowing("?tab=following", true, signedIn("viewer-1"));

    const items = result.current.items ?? [];
    expect(items.length).toBeGreaterThan(0);
    // usr-2 hosts evt-thursday-run and is deliberately outside the followed set,
    // so the tab is a subset of browse rather than a copy of it.
    expect(items.map((e) => e.id)).not.toContain("evt-thursday-run");
    expect(items.every((e) => e.hostId === "usr-host")).toBe(true);
  });

  it("keeps the browse filters working on the narrowed feed", () => {
    const { result } = renderFollowing("?tab=following&tag=running", true, signedIn("viewer-1"));

    // The only fixture tagged #running is the one hosted by an unfollowed
    // creator, so the intersection is empty - the empty state the page renders,
    // reached through the same filter bar as browse.
    expect(result.current.items).toEqual([]);
    expect(result.current.totalCount).toBe(0);
  });

  it("requests the viewer-scoped endpoint, not the browse one", () => {
    // The hook builds its key from this function when it is not under fixtures,
    // so the URL's shape is the request's shape: the me/* path carries the
    // viewer (from the bearer token), and `tab=` is dropped because the browse
    // endpoint has no such parameter and would ignore it in silence.
    const key = followingEventsKey(parseQuery("?tab=following&q=run"), "http://api.test");
    expect(key).toBe("http://api.test/api/events/me/following?q=run");
    expect(buildEventsKey(parseQuery("?tab=following&q=run"), "http://api.test", true)).toBe(
      "http://api.test/api/events?q=run",
    );
  });
});
