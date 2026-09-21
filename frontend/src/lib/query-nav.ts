import {
  DEFAULT_QUERY,
  parseQuery,
  queryToSearchParams,
} from "@/lib/query";
import type { EventQuery } from "@/types/events";

/**
 * One entry point for filter writes so every patch goes through
 * `startTransition` on the caller side (INP budget, spec §11).
 */

export function readQuery(searchParams: URLSearchParams): EventQuery {
  return parseQuery(searchParams);
}

export function writeQuery(query: EventQuery): string {
  const search = queryToSearchParams(query).toString();
  return search ? `?${search}` : "";
}

export function patchQuery(
  current: EventQuery,
  patch: Partial<EventQuery>,
): EventQuery {
  // Any filter change resets pagination — page 3 of a narrower result set is
  // never what the user meant.
  const touchesFilters = Object.keys(patch).some((k) => k !== "page");
  const next = { ...current, ...patch };
  return touchesFilters ? { ...next, page: 1 } : next;
}

export function resetQuery(): EventQuery {
  return { ...DEFAULT_QUERY };
}
