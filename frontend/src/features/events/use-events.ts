"use client";

import { useCallback, useMemo, useState } from "react";
import useSWR, { mutate as globalMutate } from "swr";
import { ApiError, API_BASE_URL, usingFixtures } from "@/lib/api";
import { fetcher } from "@/lib/swr-fetcher";
import { DEFAULT_QUERY, parseQuery, buildEventsKey } from "@/lib/query";
import { request } from "@/lib/api";
import { fixtureDetail, fixtureList } from "@/lib/fixtures";
import type { EventDetail, EventListItem, EventQuery, JoinFailure, Paged } from "@/types/events";

export const eventsKey = (query = DEFAULT_QUERY) => buildEventsKey(query, API_BASE_URL);

export const detailKey = (id: string) => `${API_BASE_URL}/api/events/${id}`;

/**
 * Applies the URL filters client-side when fixtures are active, so offline dev
 * exercises exactly the same filter/sort/empty paths as the real API. The API
 * owns this logic in production; this mirrors its contract, it does not replace
 * it (spec §12).
 */
function filterFixtures(query: EventQuery): Paged<EventListItem> {
  let items = fixtureList();
  if (query.sport) items = items.filter((e) => e.sport === query.sport);
  if (query.q) {
    const needle = query.q.toLowerCase();
    items = items.filter(
      (e) =>
        e.title.toLowerCase().includes(needle) ||
        e.venueName.toLowerCase().includes(needle),
    );
  }
  if (query.radiusKm) {
    items = items.filter((e) => (e.distanceKm ?? 0) <= query.radiusKm!);
  }
  if (query.date !== "any") {
    const limit = query.date === "today" ? 1 : 7;
    const cutoff = Date.now() + limit * 86_400_000;
    items = items.filter((e) => new Date(e.startAt).getTime() <= cutoff);
  }
  items = [...items].sort((a, b) =>
    query.sort === "distance"
      ? (a.distanceKm ?? 0) - (b.distanceKm ?? 0)
      : new Date(a.startAt).getTime() - new Date(b.startAt).getTime(),
  );
  return { items, page: 1, pageSize: items.length, totalCount: items.length };
}

export function useEvents(search?: URLSearchParams) {
  const searchKey = search?.toString() ?? "";
  const query = useMemo(() => parseQuery(searchKey), [searchKey]);
  const key = usingFixtures ? null : eventsKey(query);
  const fallback = useMemo(
    () => (usingFixtures ? filterFixtures(parseQuery(searchKey)) : undefined),
    [searchKey],
  );
  const { data, error, isLoading, isValidating, mutate } = useSWR<Paged<EventListItem>>(
    key,
    fetcher,
    { fallbackData: fallback, revalidateOnFocus: true, keepPreviousData: true },
  );
  return {
    query,
    items: data?.items,
    totalCount: data?.totalCount,
    error,
    isLoading: usingFixtures ? false : isLoading,
    isValidating,
    mutate,
  };
}

export function useEventDetail(id: string) {
  const key = usingFixtures ? null : detailKey(id);
  const fallback = useMemo(
    () => (usingFixtures ? fixtureDetail(id) : undefined),
    [id],
  );
  const { data, error, isLoading, mutate } = useSWR<EventDetail>(key, fetcher, {
    fallbackData: fallback,
  });
  return {
    event: data ?? fallback,
    error,
    isLoading: usingFixtures ? false : isLoading,
    mutate,
  };
}

export interface JoinResult {
  failure: JoinFailure | null;
  isJoining: boolean;
  isLeaving: boolean;
  join: () => Promise<{ ok: boolean; count: number }>;
  leave: () => Promise<{ ok: boolean; count: number }>;
  clearFailure: () => void;
}

/**
 * Owns the optimistic write + rollback for one event. `join()` never throws:
 * it resolves with the count the UI should show, plus the failure class so
 * JoinButton can pick its copy (spec §7).
 */
export function useJoinEvent(event: EventDetail): JoinResult {
  const [isJoining, setJoining] = useState(false);
  const [isLeaving, setLeaving] = useState(false);
  const [failure, setFailure] = useState<JoinFailure | null>(null);

  const rollback = useCallback(
    (previous: number, reason: JoinFailure) => {
      void globalMutate(
        detailKey(event.id),
        (current) =>
          current ? { ...current, participantCount: previous } : current,
        { revalidate: false },
      );
      void globalMutate(eventsKey(), undefined, { revalidate: true });
      setFailure(reason);
    },
    [event.id],
  );

  const optimisticallySet = useCallback(
    (next: number) => {
      void globalMutate(
        detailKey(event.id),
        (current) => (current ? { ...current, participantCount: next } : current),
        { revalidate: false },
      );
    },
    [event.id],
  );

  const join = useCallback(async () => {
    const previous = event.participantCount;
    const optimistic = Math.min(previous + 1, event.maxParticipants);
    setFailure(null);
    setJoining(true);
    optimisticallySet(optimistic);
    try {
      const result = await postParticipants(event.id, "POST");
      const count = result?.participantCount ?? optimistic;
      optimisticallySet(count);
      void globalMutate(eventsKey(), undefined, { revalidate: true });
      return { ok: true, count };
    } catch (error) {
      const reason =
        error instanceof ApiError ? error.joinFailure : "network";
      rollback(previous, reason);
      return { ok: false, count: previous };
    } finally {
      setJoining(false);
    }
  }, [event.id, event.maxParticipants, event.participantCount, optimisticallySet, rollback]);

  const leave = useCallback(async () => {
    const previous = event.participantCount;
    const optimistic = Math.max(previous - 1, 0);
    setFailure(null);
    setLeaving(true);
    optimisticallySet(optimistic);
    try {
      const result = await postParticipants(event.id, "DELETE");
      const count = result?.participantCount ?? optimistic;
      optimisticallySet(count);
      void globalMutate(eventsKey(), undefined, { revalidate: true });
      return { ok: true, count };
    } catch (error) {
      const reason = error instanceof ApiError ? error.joinFailure : "network";
      rollback(previous, reason);
      return { ok: false, count: previous };
    } finally {
      setLeaving(false);
    }
  }, [event.id, event.participantCount, optimisticallySet, rollback]);

  return {
    failure,
    isJoining,
    isLeaving,
    join,
    leave,
    clearFailure: useCallback(() => setFailure(null), []),
  };
}

async function postParticipants(
  id: string,
  method: "POST" | "DELETE",
): Promise<{ participantCount: number }> {
  if (usingFixtures) {
    throw new ApiError({
      status: 0,
      title: "Not connected",
      detail: "Set NEXT_PUBLIC_API_BASE_URL to join real events.",
      code: "Unknown",
    });
  }
  const result = await request<{ participantCount: number } | undefined>(
    `/api/events/${id}/participants`,
    { method },
  );
  // A 204 carries no body; the caller falls back to its own optimistic count.
  return result ?? { participantCount: -1 };
}
