import { useMemo, useSyncExternalStore } from "react";
import type { EventDateFilter, EventQuery, EventSort, SportKey } from "@/types/events";
import { RADIUS_OPTIONS } from "@/lib/sports";

/**
 * Filters live in the URL (spec principle 2), so `/` is shareable, back-button
 * correct, and server-renderable. Reads are cheap; writes go through
 * `startTransition` so typing never blocks paint (INP < 200ms).
 */

export const DEFAULT_QUERY: EventQuery = {
  q: null,
  sport: null,
  date: "week",
  radiusKm: null,
  sort: "startAt",
  page: 1,
};

const DATES: readonly EventDateFilter[] = ["today", "week", "any"];
const SORTS: EventSort[] = ["startAt", "distance"];

export function queryToSearchParams(query: EventQuery): URLSearchParams {
  const p = new URLSearchParams();
  if (query.q) p.set("q", query.q);
  if (query.sport) p.set("sport", query.sport);
  if (query.date !== DEFAULT_QUERY.date) p.set("date", query.date);
  if (query.radiusKm) p.set("radius", String(query.radiusKm));
  if (query.sort !== DEFAULT_QUERY.sort) p.set("sort", query.sort);
  if (query.page > 1) p.set("page", String(query.page));
  return p;
}

/** Tolerant parse: junk in the URL falls back to defaults, never crashes SSR. */
export function parseQuery(search: string | URLSearchParams): EventQuery {
  const p = typeof search === "string" ? new URLSearchParams(search) : search;
  const sport = p.get("sport");
  const date = p.get("date");
  const sort = p.get("sort");
  const radiusRaw = Number(p.get("radius"));
  const pageRaw = Number(p.get("page"));
  const q = p.get("q");

  return {
    q: q && q.trim() ? q.trim() : null,
    sport: sport && isSportKey(sport) ? sport : null,
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

function isSportKey(value: string): value is SportKey {
  return [
    "badminton",
    "basketball",
    "running",
    "soccer",
    "tennis",
    "cricket",
    "netball",
    "volleyball",
  ].includes(value);
}

/** Number of filters a user would consider "on" — drives `Filters · 3`. */
export function activeFilterCount(query: EventQuery): number {
  let n = 0;
  if (query.sport) n += 1;
  if (query.date !== DEFAULT_QUERY.date) n += 1;
  if (query.radiusKm) n += 1;
  if (query.sort !== DEFAULT_QUERY.sort) n += 1;
  return n;
}

export function buildEventsKey(
  query: EventQuery,
  origin?: string,
): string {
  const base = `${origin ?? ""}/api/events`;
  const search = queryToSearchParams(query).toString();
  return search ? `${base}?${search}` : base;
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
