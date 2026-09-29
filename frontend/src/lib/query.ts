import { useMemo, useSyncExternalStore } from "react";
import type { EventDateFilter, EventQuery, EventSort } from "@/types/events";
import { normalizeTag, RADIUS_OPTIONS } from "@/lib/sports";

/**
 * Filters live in the URL (spec principle 2), so `/` is shareable, back-button
 * correct, and server-renderable. Reads are cheap; writes go through
 * `startTransition` so typing never blocks paint (INP < 200ms).
 */

export const DEFAULT_QUERY: EventQuery = {
  q: null,
  tag: null,
  date: "week",
  radiusKm: null,
  sort: "startAt",
  page: 1,
};

const DATES: readonly EventDateFilter[] = ["today", "week", "any"];
const SORTS: EventSort[] = ["startAt", "distance"];

/**
 * The URL param holding the activity filter. It is `event`, not `sport`, and it
 * is a *browse-URL* name only: the wire param the .NET API expects is still
 * `sport` (`EventsController.List([FromQuery] string? sport)`), so
 * `buildEventsKey(..., keyForApi: true)` renames it on the way out. Renaming the
 * API param too would need both repositories redeployed at once, which a
 * shareable URL outlives.
 */
export const EVENT_PARAM = "tag";

/**
 * Historical param names for the same filter. A browse URL is something users
 * paste into chat and leave in bookmarks for months, so every name this filter
 * has ever used keeps parsing: `?event=tennis` and `?sport=tennis` both still
 * mean `?tag=tennis`.
 */
export const LEGACY_EVENT_PARAMS = ["event", "sport"] as const;

/** The param the API reads. `EVENT_PARAM` is what the address bar shows. */
export const API_SPORT_PARAM = "tag";


export function queryToSearchParams(query: EventQuery): URLSearchParams {
  const p = new URLSearchParams();
  if (query.q) p.set("q", query.q);
  if (query.tag) p.set(EVENT_PARAM, query.tag);
  if (query.date !== DEFAULT_QUERY.date) p.set("date", query.date);
  if (query.radiusKm) p.set("radius", String(query.radiusKm));
  if (query.sort !== DEFAULT_QUERY.sort) p.set("sort", query.sort);
  if (query.page > 1) p.set("page", String(query.page));
  return p;
}

/**
 * Tolerant parse: junk in the URL falls back to defaults, never crashes SSR.
 *
 * The activity filter is read from `?event=`; `?sport=` still parses so links
 * shared before the rename keep working.
 */
export function parseQuery(search: string | URLSearchParams): EventQuery {
  const p = typeof search === "string" ? new URLSearchParams(search) : search;
  const tag = normalizeTag(p.get(EVENT_PARAM) ?? firstLegacyTag(p));
  const date = p.get("date");
  const sort = p.get("sort");
  const radiusRaw = Number(p.get("radius"));
  const pageRaw = Number(p.get("page"));
  const q = p.get("q");

  return {
    q: q && q.trim() ? q.trim() : null,
    tag,
    date: date && (DATES as readonly string[]).includes(date)
      ? (date as EventDateFilter)
      : DEFAULT_QUERY.date,
    radiusKm: RADIUS_OPTIONS.includes(radiusRaw as (typeof RADIUS_OPTIONS)[number])
      ? radiusRaw
      : null,
    sort: sort && SORTS.includes(sort as EventSort) ? (sort as EventSort) : DEFAULT_QUERY.sort,
    page: Number.isInteger(pageRaw) && pageRaw > 0 ? pageRaw : 1,
  };
}

/** `?event=`/`?sport=` from before the filter was renamed to `tag`. */
function firstLegacyTag(p: URLSearchParams): string | null {
  for (const name of LEGACY_EVENT_PARAMS) {
    const value = p.get(name);
    if (value) return value;
  }
  return null;
}

/** Number of filters a user would consider "on" — drives `Filters · 3`. */
export function activeFilterCount(query: EventQuery): number {
  let n = 0;
  if (query.tag) n += 1;
  if (query.date !== DEFAULT_QUERY.date) n += 1;
  if (query.radiusKm) n += 1;
  if (query.sort !== DEFAULT_QUERY.sort) n += 1;
  return n;
}

/**
 * SWR cache key / actual request URL for the listing.
 *
 * `keyForApi` is true when the key is a real endpoint. The activity filter used
 * to need renaming here (`event` in the address bar, `sport` on the wire); with
 * tags both sides say `tag`, so the URL form is already the API form and no
 * translation happens. Under fixtures the key is only a cache identity
 * (`useEvents` never fetches it), so it stays in URL form.
 *
 * The default `date` filter is deliberately left out of the key. It is "this
 * week" and the API returns only upcoming events, so on any given day the default
 * window covers every event in the system: keeping it in the key would hand the
 * default browse page and every `?date=` link two identical copies of the list,
 * and an unrelated filter change would then force a refetch instead of reading
 * the shared cache. A change to `date` alone becomes a cache hit rather than a
 * request, which is why it is safe to omit; drop this if the backend ever starts
 * seeding events further out than seven days.
 */
export function buildEventsKey(query: EventQuery, origin?: string, keyForApi = false): string {
  const search = queryToSearchParams(query);
  if (keyForApi) {
    if (query.date === DEFAULT_QUERY.date) search.delete("date");
  }
  const base = `${origin ?? ""}/api/events`;
  const formatted = search.toString();
  return formatted ? `${base}?${formatted}` : base;
}

/** Subscribe to `popstate` so the back button updates filters without a reload. */
const subscribe = (callback: () => void) => {
  window.addEventListener("popstate", callback);
  return () => window.removeEventListener("popstate", callback);
};

const getSnapshot = () => window.location.search;
const getServerSnapshot = () => "";

export function useSearchParamsValue(): URLSearchParams {
  const search = useSyncExternalStore(subscribe, getSnapshot, getServerSnapshot);
  return useMemo(() => new URLSearchParams(search), [search]);
}
