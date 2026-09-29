import { describe, expect, it } from "vitest";
import {
  DEFAULT_QUERY,
  buildEventsKey,
  parseQuery,
  queryToSearchParams,
} from "@/lib/query";
import type { EventQuery } from "@/types/events";

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
