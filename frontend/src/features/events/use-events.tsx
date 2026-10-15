"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import useSWR, { mutate as globalMutate } from "swr";
import { ApiError, API_BASE_URL, usingFixtures } from "@/lib/api";
import { fetcher } from "@/lib/swr-fetcher";
import { DEFAULT_QUERY, parseQuery, buildEventsKey, followingEventsKey } from "@/lib/query";
import { request } from "@/lib/api";
import { fixtureDetail, fixtureList, fixturePendingJoinRequestIds, fixturePopularTags } from "@/lib/fixtures";

import { useAuth } from "@/lib/auth/auth-provider";
import type {
  EventAnalytics,
  EventDetail,
  EventListItem,
  EventQuery,
  JoinFailure,
  JoinRequestList,
  Paged,
  PopularTag,
} from "@/types/events";

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

/** `GET /api/events/me/following` — the Following feed. Viewer-scoped like the
 *  other me/* routes: the id never appears in the URL, the bearer token decides. */
export const followingEventsKeyFor = (query: EventQuery) =>
  followingEventsKey(query, API_BASE_URL);

export const detailKey = (id: string) => `${API_BASE_URL}/api/events/${id}`;

/** `GET /api/events/me/joined` — the ids the current viewer has a participant row on. */
export const joinedKey = () => `${API_BASE_URL}/api/events/me/joined`;

/** `GET /api/events/me/interested` — the ids the current viewer has an interest row on. */
export const interestedKey = () => `${API_BASE_URL}/api/events/me/interested`;

/** `GET /api/events/me/pending-join-requests` — the ids with a join request from the
 *  current viewer that the host has not answered yet. */
export const pendingJoinRequestsKey = () =>
  `${API_BASE_URL}/api/events/me/pending-join-requests`;

/** `GET /api/events/me/hosting` — every event owned by the current host. */
export const hostedKey = () => `${API_BASE_URL}/api/events/me/hosting`;

export const joinRequestsKey = (id: string, includeDecided = false) =>
  `${API_BASE_URL}/api/events/${id}/join-requests${includeDecided ? "?includeDecided=true" : ""}`;

/** `GET /api/events/{id}/analytics` — host-only Event Insights. */
export const analyticsKey = (id: string) => `${API_BASE_URL}/api/events/${id}/analytics`;

/** The offline stand-in for the view count. Fixed, so the fixture panel renders the
 *  same number on every run — see `useEventAnalytics`. */
const FIXTURE_VIEWS = 42;

interface JoinedEnvelope {
  eventIds: string[];
}

interface InterestedEnvelope {
  eventIds: string[];
}

interface PendingJoinRequestsEnvelope {
  eventIds: string[];
}

/**
 * Applies the URL filters client-side when fixtures are active, so offline dev
 * exercises exactly the same filter/sort/empty paths as the real API. The API
 * owns this logic in production; this mirrors its contract, it does not replace
 * it (spec §12).
 */
function filterFixtures(query: EventQuery, followedHostIds?: ReadonlySet<string>): Paged<EventListItem> {
  let items = fixtureList();
  // The offline twin of the server's follow-graph narrowing: when a set arrives,
  // only events hosted by those ids survive. One pass, mirroring the single
  // EXISTS over user_follows the API answers with.
  if (followedHostIds) items = items.filter((e) => followedHostIds.has(e.hostId ?? ""));
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

/**
 * The host ids the offline Following-tab mirror treats as followed. The live
 * feed asks the server, which reads `user_follows`; fixtures have no database,
 * so the relation is this fixed set: the default fixture creator plus nobody
 * else. `evt-thursday-run` (hosted by a roster member) is the seed this leaves
 * out, which is what makes the empty and filtered states reachable offline.
 */
const FOLLOWED_FIXTURE_HOST_IDS = new Set<string>(["usr-host"]);

/**
 * The Following feed (`?tab=following`).
 *
 * Three states, and the page must tell them apart:
 *  - signed out (`user === null`): no request at all. The endpoint would answer
 *    401, and a prompt is friendlier than a swallowed error; the browse page
 *    shows a sign-in prompt instead of events.
 *  - still resolving the session (`user === undefined` via loading — the caller
 *    passes `null` only once auth has settled): also no request, so the token
 *    store has an id before a viewer-scoped key is built.
 *  - signed in: one request per filter state, same paged envelope as browse.
 *
 * Under fixtures the fetch is replaced by the same client-side mirror
 * `filterFixtures` runs for browse, narrowed to followed hosts, so the tab is
 * exercisable with no API running.
 */
export function useFollowingEvents(search?: URLSearchParams, enabled = false) {
  const searchKey = search?.toString() ?? "";
  const query = useMemo(() => parseQuery(searchKey), [searchKey]);
  const { user } = useAuth();
  const active = enabled && Boolean(user?.id);
  const key = usingFixtures || !active ? null : followingEventsKeyFor(query);
  const fallback = useMemo(
    () =>
      usingFixtures && enabled
        ? filterFixtures({ ...parseQuery(searchKey), tab: "all" }, FOLLOWED_FIXTURE_HOST_IDS)
        : undefined,
    [searchKey, enabled],
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
    isLoading: usingFixtures ? false : active && isLoading,
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
        (current) => (current ? { ...current, joinedCount: previous } : current),
        { revalidate: false },
      );
      setFailure(reason);
    },
    [event.id],
  );

  const run = useCallback(
    async (method: "POST" | "DELETE"): Promise<{ ok: boolean; count: number }> => {
      setFailure(null);
      const previous = event.joinedCount;
      const optimistic = method === "POST" ? previous + 1 : Math.max(0, previous - 1);
      void globalMutate(
        detailKey(event.id),
        (current) =>
          current ? { ...current, joinedCount: optimistic } : current,
        { revalidate: false },
      );
      try {
        if (method === "POST") setJoining(true);
        else setLeaving(true);
        const data = await request<{ joinedCount: number } | undefined>(
          `/api/events/${event.id}/participants`,
          { method },
        );
        const count = data?.joinedCount ?? optimistic;
        void globalMutate(
          detailKey(event.id),
          (current) => (current ? { ...current, joinedCount: count } : current),
          { revalidate: false },
        );
        // The joined set (My events, and every card's Join/Leave state) changed.
        void globalMutate(joinedKey(), undefined, { revalidate: true });
        // So did the pending-request set: a gated join added this event to it, and
        // leaving an event the viewer had requested clears it the same way.
        void globalMutate(pendingJoinRequestsKey(), undefined, { revalidate: true });
        // Joining clears any interest server-side, so the interested set and the
        // browse list's interestedCount both move too.
        void globalMutate(interestedKey(), undefined, { revalidate: true });
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
    [event.id, event.joinedCount, rollback],
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

/**
 * `GET /api/events/me/interested`. The server-backed mirror of the old
 * localStorage interest store and the exact twin of `useJoinedEvents`: signed-out
 * or fixture mode resolves to an empty set without blocking the page, because the
 * interested set only decorates an already-working list. `useToggleInterest`
 * revalidates `interestedKey()` after a toggle so this refreshes, and joining
 * clears interest server-side so the two sets never overlap.
 */
export function useInterestedEvents() {
  const key = usingFixtures ? null : interestedKey();
  const { data, error, isLoading, mutate } = useSWR<InterestedEnvelope, Error>(key, fetcher, {
    revalidateOnFocus: false,
  });
  // Stable reference while the contents match, for the same reason as useJoinedEvents.
  const rawIds = data?.eventIds;
  const idsKey = rawIds?.join(",") ?? "";
  // eslint-disable-next-line react-hooks/exhaustive-deps -- idsKey is the content identity of rawIds
  const ids = useMemo(() => rawIds ?? EMPTY_IDS, [idsKey]);
  const isInterested = useCallback((id: string) => ids.includes(id), [ids]);
  return { ids, isInterested, error, isLoading, mutateInterested: mutate };
}

/**
 * `GET /api/events/me/pending-join-requests`. The exact twin of `useJoinedEvents`
 * again: signed-out or fixture mode resolves to an empty set without blocking the
 * page, because the pending set only decorates an already-working list. It is what
 * lets a browse card hold a member in "Request submitted" between page loads —
 * without it, the state exists only until the next navigation.
 * `useJoinEvent`/`useCardJoin` revalidate `pendingJoinRequestsKey()` after a join
 * so this refreshes; the host's decision arrives through the next revalidation of
 * the joined/pending pair.
 */
export function usePendingJoinRequests() {
  const key = usingFixtures ? null : pendingJoinRequestsKey();
  const { data, error, isLoading, mutate } = useSWR<PendingJoinRequestsEnvelope, Error>(
    key,
    fetcher,
    { revalidateOnFocus: true },
  );
  // Stable reference while the contents match, for the same reason as useJoinedEvents.
  const rawIds = usingFixtures ? fixturePendingJoinRequestIds() : data?.eventIds;
  const idsKey = rawIds?.join(",") ?? "";
  // eslint-disable-next-line react-hooks/exhaustive-deps -- idsKey is the content identity of rawIds
  const ids = useMemo(() => rawIds ?? EMPTY_IDS, [idsKey]);
  const isRequestPending = useCallback((id: string) => ids.includes(id), [ids]);
  return { ids, isRequestPending, error, isLoading, mutatePending: mutate };
}

/**
 * The host's Join Requests queue for one event, plus the approve/reject actions.
 *
 * Mounted only by a host who owns the event: the endpoint is host-scoped and
 * answers 404 to anyone else, so fetching it for a stranger's event would put the
 * panel in an error state for no reason.
 *
 * Returns the list keyed on includeDecided so the pending-only queue and the full
 * history are separate cache entries — toggling the filter cannot make the two
 * states flicker through each other.
 */
export function useJoinRequests(eventId: string, includeDecided = false) {
  const key = usingFixtures ? null : joinRequestsKey(eventId, includeDecided);
  const { data, error, isLoading, mutate } = useSWR<JoinRequestList, Error>(key, fetcher, {
    revalidateOnFocus: true,
  });

  const decide = useCallback(
    async (userId: string, action: "approve" | "reject") => {
      await request<JoinRequestList>(`/api/events/${eventId}/join-requests/${userId}/${action}`, {
        method: "POST",
      });
      // Both cache entries move: the pending queue loses the row, and the history
      // gains it with its new status. Settling from the response body rather than
      // re-fetching means the list the host sees is the one the server wrote.
      void globalMutate(joinRequestsKey(eventId, false), undefined, { revalidate: true });
      void globalMutate(joinRequestsKey(eventId, true), undefined, { revalidate: true });
      // Approval takes a spot, so the event's own count and every card's fullness
      // are now stale too.
      void globalMutate(detailKey(eventId), undefined, { revalidate: true });
      void globalMutate(eventsKey(), undefined, { revalidate: true });
      void globalMutate(joinedKey(), undefined, { revalidate: true });
      // The requester's "Request submitted" card state clears when the host answers,
      // so the pending set is invalidated from the host's own action.
      void globalMutate(pendingJoinRequestsKey(), undefined, { revalidate: true });
    },
    [eventId],
  );

  const approve = useCallback((userId: string) => decide(userId, "approve"), [decide]);
  const reject = useCallback((userId: string) => decide(userId, "reject"), [decide]);

  return {
    items: data?.items ?? [],
    pendingCount: data?.pendingCount ?? 0,
    error,
    isLoading: usingFixtures ? false : isLoading,
    approve,
    reject,
    mutate,
  };
}

/**
 * The host's Event Insights for one event.
 *
 * `enabled` is the caller's host check, and it is load-bearing rather than an
 * optimisation: the endpoint 404s for anyone who is not the host, so fetching it
 * unconditionally would put a stranger's detail page into an error state to report a
 * number they are never allowed to see. Same reasoning as `useJoinRequests`.
 *
 * Under fixtures the two public counts come from the detail the page already holds
 * and `views` is a fixed stand-in — there is no view table offline, and inventing a
 * growing number would make the fixture panel non-deterministic under Vitest.
 *
 * `revalidateOnFocus` is on because a view is recorded by the detail read itself, so
 * returning to the tab is genuinely new information — the panel would otherwise show
 * a total from before the visit that triggered it.
 */
export function useEventAnalytics(eventId: string, enabled: boolean) {
  const key = usingFixtures || !enabled ? null : analyticsKey(eventId);
  const { data, error, isLoading } = useSWR<EventAnalytics, Error>(key, fetcher, {
    revalidateOnFocus: true,
  });

  const fallback = useMemo<EventAnalytics | null>(() => {
    if (!usingFixtures || !enabled) return null;
    const detail = fixtureDetail(eventId);
    if (!detail) return null;
    return {
      views: FIXTURE_VIEWS,
      joinedCount: detail.joinedCount,
      interestedCount: detail.interestedCount,
    };
  }, [eventId, enabled]);

  return {
    analytics: data ?? fallback,
    error,
    isLoading: usingFixtures ? false : isLoading,
  };
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

/**
 * Whether the current viewer hosts a given event, as the id-set twin of
 * `useJoinedEvents`. A browse card cannot see `isHost` (the list DTO omits it),
 * so without this it offers an enabled Join on the viewer's own event and the
 * API rejects it with "You cannot join an event you are hosting" (422), which
 * surfaces as a misleading "Try again". Deriving host-ness from `me/hosting`
 * lets the card render the disabled "You're hosting" state instead.
 *
 * Shares `hostedKey()` with `useHostedEvents` (same SWR cache entry), so it adds
 * no extra request when that hook is already mounted. Anonymous-tolerant like
 * the joined set: no identity resolves to an empty set, never an error state.
 */
export function useIsHosting() {
  const { items } = useHostedEvents();
  const rawIds = useMemo(() => items.map((item) => item.id), [items]);
  // Stable reference while the contents match, for the same reason as the joined
  // set: a fresh array per revalidation would rebuild every consumer each render.
  const idsKey = rawIds.join(",");
  // eslint-disable-next-line react-hooks/exhaustive-deps -- idsKey is the content identity of rawIds
  const ids = useMemo(() => rawIds, [idsKey]);
  const isHosting = useCallback((id: string) => ids.includes(id), [ids]);
  return { ids, isHosting };
}

const EMPTY_IDS: string[] = [];

/** A detail payload satisfies every `EventListItem` field; keep only those. */
function toCard(detail: EventDetail): EventListItem {
  const { host, isHost, isJoined, isInterested, participants, cancelledAt, description, ...card } = detail;
  void host; void isHost; void isJoined; void isInterested; void participants; void cancelledAt; void description;
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
  const interests = useInterestedEvents();
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
    // The Interested-tab membership, so the view can bucket the same resolved
    // cards into "interested but not joined" without a second fetch.
    interestedIds: interests.ids,
    error: joined.error,
    isLoading: usingFixtures ? false : joined.isLoading,
  };
}


