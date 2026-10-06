"use client";

import { useCallback, useState } from "react";
import { useSWRConfig } from "swr";
import { ApiError, request } from "@/lib/api";
import { detailKey, eventsKey, interestedKey, useInterestedEvents } from "./use-events";
import type { JoinFailure } from "@/types/events";

/**
 * The signed-in user's Interested set, now server-backed.
 *
 * Interest used to live in localStorage (per-uid). It is now a real
 * `event_interests` row, so this hook reads membership from
 * `GET /api/events/me/interested` (via `useInterestedEvents`, the twin of
 * `useJoinedEvents`) and writes through `POST /api/events/{id}/interest`. The
 * consequence the UI relies on: interest follows the account across devices, and
 * joining an event clears the interest row server-side, so the Interested and
 * Joined sets never overlap.
 */
export function useInterests() {
  const { isInterested, ids } = useInterestedEvents();
  return { ids, isInterested };
}

/**
 * Toggle one event's interest. Mirrors `useCardJoin`: performs the call,
 * invalidates the caches the card and My-events page read (the interested set,
 * this event's detail for its isInterested flag, and the browse list for its
 * interestedCount), and reports the failure class so the button can surface a
 * retry rather than silently reverting. Returns the server's post-toggle state.
 */
export function useToggleInterest(eventId: string) {
  const { mutate } = useSWRConfig();
  const [pending, setPending] = useState(false);
  const [failure, setFailure] = useState<JoinFailure | null>(null);

  const toggle = useCallback(
    async (): Promise<{ ok: boolean; isInterested: boolean }> => {
      setFailure(null);
      setPending(true);
      try {
        // The endpoint is a toggle, so the verb only expresses intent; both settle
        // on the same handler. The server returns the post-toggle state, surfaced
        // to the caller for its toast rather than guessed from a local flip.
        const result = await request<{ isInterested: boolean } | undefined>(
          `/api/events/${eventId}/interest`,
          { method: "POST" },
        );
        void mutate(interestedKey(), undefined, { revalidate: true });
        void mutate(detailKey(eventId), undefined, { revalidate: true });
        void mutate(eventsKey(), undefined, { revalidate: true });
        return { ok: true, isInterested: result?.isInterested ?? false };
      } catch (error) {
        setFailure(error instanceof ApiError ? error.joinFailure : "network");
        return { ok: false, isInterested: false };
      } finally {
        setPending(false);
      }
    },
    [eventId, mutate],
  );

  return { toggle, failure, isPending: pending };
}
