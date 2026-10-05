"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import useSWR, { mutate as globalMutate } from "swr";
import { ApiError, API_BASE_URL, usingFixtures } from "@/lib/api";
import { fetcher } from "@/lib/swr-fetcher";
import { DEFAULT_QUERY, parseQuery, buildEventsKey } from "@/lib/query";
import { request } from "@/lib/api";
import { fixtureDetail, fixtureList, fixturePopularTags } from "@/lib/fixtures";
import { useAuth } from "@/lib/auth/auth-provider";
import { useInterests } from "./use-interests";
import type { EventDetail, EventListItem, EventQuery, JoinFailure, Paged, PopularTag } from "@/types/events";

/** `GET /api/tags/popular` - the home page's filter chips. */
export const popularTagsKey = () => `${API_BASE_URL}/api/tags/popular`;

/**
 * The popular-tag chips' data. Under fixtures it derives from the fixture list
 * rather than fetching, so the style guide, Vitest and Playwright all get the
 * same chips and none of them hit a network.
 *
 * Empty is a valid, expected result (a database with no tagged events), not an
 * error - the caller hides the chip row rather than showing a broken control.
 */
export function usePopularTags(): { tags: PopularTag[]; isLoading: boolean } {
  const { data, isLoading } = useSWR<PopularTag[]>(
    usingFixtures ? null : popularTagsKey(),
    fetcher,
    { revalidateOnFocus: false },
  );

  const tags = useMemo(() => {
    if (usingFixtures) return fixturePopularTags();
    return data ?? [];
  }, [data]);

  return { tags, isLoading: usingFixtures ? false : isLoading };
}



export const eventsKey = (query = DEFAULT_QUERY) => buildEventsKey(query, API_BASE_URL, !usingFixtures);

export const detailKey = (id: string) => `${API_BASE_URL}/api/events/${id}`;

/** `GET /api/events/me/joined` — the ids the current viewer has a participant row on. */
export const joinedKey = () => `${API_BASE_URL}/api/events/me/joined`;

/** `GET /api/events/me/hosting` — every event owned by the current host. */
export const hostedKey = () => `${API_BASE_URL}/api/events/me/hosting`;

interface JoinedEnvelope {
  eventIds: string[];
}

/**
 * Applies the URL filters client-side when fixtures are active, so offline dev
 * exercises exactly the same filter/sort/empty paths as the real API. The API
 * owns this logic in production; this mirrors its contract, it does not replace
 * it (spec §12).
 */
function filterFixtures(query: EventQuery): Paged<EventListItem> {
  let items = fixtureList();
  if (query.tag) items = items.filter((e) => e.tags.includes(query.tag!));
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
        (current) => (current ? { ...current, participantCount: previous } : current),
        { revalidate: false },
      );
      setFailure(reason);
    },
    [event.id],
  );

  const run = useCallback(
    async (method: "POST" | "DELETE"): Promise<{ ok: boolean; count: number }> => {
      setFailure(null);
      const previous = event.participantCount;
      const optimistic = method === "POST" ? previous + 1 : Math.max(0, previous - 1);
      void globalMutate(
        detailKey(event.id),
        (current) =>
          current ? { ...current, participantCount: optimistic } : current,
        { revalidate: false },
      );
      try {
        if (method === "POST") setJoining(true);
        else setLeaving(true);
        const data = await request<{ participantCount: number } | undefined>(
          `/api/events/${event.id}/participants`,
          { method },
        );
        const count = data?.participantCount ?? optimistic;
        void globalMutate(
          detailKey(event.id),
          (current) => (current ? { ...current, participantCount: count } : current),
          { revalidate: false },
        );
        // The joined set (My events, and every card's Join/Leave state) changed.
        void globalMutate(joinedKey(), undefined, { revalidate: true });
        void globalMutate(eventsKey(), undefined, { revalidate: true });
        return { ok: true, count };
      } catch (error) {
        rollback(previous, error instanceof ApiError ? error.joinFailure : "network");
        return { ok: false, count: previous };
      } finally {
        setJoining(false);
        setLeaving(false);
      }
    },
    [event.id, event.participantCount, rollback],
  );

  const join = useCallback(() => run("POST"), [run]);
  const leave = useCallback(() => run("DELETE"), [run]);
  const clearFailure = useCallback(() => setFailure(null), []);

  return { failure, isJoining, isLeaving, join, leave, clearFailure };
}


/**
 * `GET /api/events/me/joined`. Signed-out or fixture mode resolves to an empty
 * set without erroring the page: the browse list must never be blocked by this
 * error state, because the joined set only decorates an already-working list.
 * `useJoinEvent` revalidates `joinedKey()` after a join/leave so this refreshes.
 */
export function useJoinedEvents() {
  const key = usingFixtures ? null : joinedKey();
  const { data, error, isLoading, mutate } = useSWR<JoinedEnvelope, Error>(key, fetcher, {
    revalidateOnFocus: false,
  });
  // Stable reference while the contents match: SWR hands back a fresh envelope
  // per revalidation, and an unstable `ids` would rebuild useMyEvents'
  // wanted -> items -> slots chain on every render (the slot effect loop).
  const rawIds = data?.eventIds;
  const idsKey = rawIds?.join(",") ?? "";
  // eslint-disable-next-line react-hooks/exhaustive-deps -- idsKey is the content identity of rawIds
  const ids = useMemo(() => rawIds ?? EMPTY_IDS, [idsKey]);
  const isJoined = useCallback((id: string) => ids.includes(id), [ids]);
  return { ids, isJoined, error, isLoading, mutateJoined: mutate };
}

export function useHostedEvents() {
  const { user, loading } = useAuth();
  const key = usingFixtures || loading || !user ? null : hostedKey();
  const { data, error, isLoading, mutate } = useSWR<EventListItem[], Error>(key, fetcher, {
    revalidateOnFocus: true,
  });
  return {
    items: usingFixtures
      ? fixtureList().filter((item) => fixtureDetail(item.id)?.isHost)
      : data ?? [],
    error,
    isLoading: usingFixtures ? false : loading || (!!user && isLoading),
    mutate,
  };
}

const EMPTY_IDS: string[] = [];

/** A detail payload satisfies every `EventListItem` field; keep only those. */
function toCard(detail: EventDetail): EventListItem {
  const { host, isHost, isJoined, participants, cancelledAt, description, ...card } = detail;
  void host; void isHost; void isJoined; void participants; void cancelledAt; void description;
  return card;
}

/**
 * Resolve one wanted event by id through its detail endpoint (`GET /api/events/{id}`
 * returns the full card fields whether upcoming or past, unlike the browse feed)
 * and render a slot. `null`/empty id renders nothing and requests nothing, which
 * lets My events keep a fixed number of slots while only showing what exists.
 */
function EventCardSlot({
  id,
  onResolved,
}: {
  id: string | null;
  onResolved: (id: string, card: EventListItem | null) => void;
}) {
  const key = usingFixtures || !id ? null : detailKey(id);
  const { data } = useSWR<EventDetail, Error>(key, fetcher, { revalidateOnFocus: false });
  // Report through a ref-held callback. `onResolved(id, card)` must NOT be
  // effect-driven on `card`: `toCard(data)` builds a fresh object each render,
  // so the effect would re-fire every render and the setState would cascade
  // forever. `data` is SWR's cached payload and changes identity only when the
  // event actually changed — the only moments worth reporting. The parent's
  // handler reads state through the functional updater, so a late call is safe.
  const report = useRef(onResolved);
  useEffect(() => {
    report.current = onResolved;
  }, [onResolved]);
  useEffect(() => {
    if (id) report.current(id, data ? toCard(data) : null);
  }, [id, data]);
  return null;
}

// Fixed slot count so My events resolves a bounded, stable number of lookups.
// Honest for demo scale; a real per-user endpoint (`GET /api/me/events`) would
// replace this fan-out entirely and lift the cap.
const MAX_CARDS = 30;
const SLOTS = Array.from({ length: MAX_CARDS }, (_, i) => i);



/**
 * The events for the My events page: everything the viewer has marked Interested
 * (localStorage, per-uid) or has Joined (the API's participant rows). Only those
 * two sets appear — My events is the personal list, not the whole feed.
 *
 * Ids are resolved through `MAX_CARDS` fixed `<EventCardSlot>` rows (a component,
 * so its hook is legal) that report back what they fetched. The slot count is
 * constant, so nothing unmounts as the wanted set changes.
 */
export function useMyEvents() {
  const joined = useJoinedEvents();
  const interests = useInterests();
  const [resolved, setResolved] = useState<Record<string, EventListItem>>({});

  const wanted = useMemo(() => {
    const set = new Set<string>(joined.ids);
    for (const id of interests.ids) set.add(id);
    return [...set];
  }, [joined.ids, interests.ids]);

  // Drop a card the moment its event leaves both sets, so Uninterest/Leave remove
  // it from My events even before its slot finishes re-fetching.
  const items = useMemo(
    () =>
      wanted
        .map((id) => resolved[id])
        .filter((c): c is EventListItem => c !== undefined)
        .sort((a, b) => new Date(a.startAt).getTime() - new Date(b.startAt).getTime()),
    [wanted, resolved],
  );

  const handleResolved = useCallback((id: string, card: EventListItem | null) => {
    setResolved((current) => {
      if (card) {
        // Keep the stored object only when it differs, so a revalidation of an
        // unchanged event never re-renders the page (identity comparison is
        // enough: the slot reports only when `data` itself changed).
        if (current[id] === card) return current;
        return { ...current, [id]: card };
      }
      if (!(id in current)) return current;
      const next = { ...current };
      delete next[id];
      return next;
    });
  }, []);

  const slotIds = useMemo(
    () => Array.from({ length: MAX_CARDS }, (_, i) => wanted[i] ?? null),
    [wanted],
  );

  return {
    items,
    slots: (
      <>
        {SLOTS.map((i) => (
          <EventCardSlot key={i} id={slotIds[i]} onResolved={handleResolved} />
        ))}
      </>
    ),
    // How each event reached the page: joined (real participant row) vs. merely
    // interested. The card uses `isJoined` to pick Join/Leave, else Interest/Uninterest.
    isJoined: joined.isJoined,
    isInterested: interests.isInterested,
    toggleInterest: interests.toggle,
    error: joined.error,
    isLoading: usingFixtures ? false : joined.isLoading,
  };
}


