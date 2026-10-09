"use client";

import { useCallback, useState } from "react";
import { useSWRConfig } from "swr";
import { ApiError, request } from "@/lib/api";
import { detailKey, eventsKey, interestedKey, joinedKey } from "./use-events";
import type { JoinFailure } from "@/types/events";

/**
 * Join / Leave for one event, driven by a browse or My-events *card* (the detail
 * page owns its own `useJoinEvent`). The card has no optimistic count to roll
 * back — it shows a count it did not author — so instead of the detail page's
 * decrement dance this does the call, invalidates the caches the card and the
 * My-events page read, and reports the failure class so the button can show a
 * "Try again" rather than silently reverting.
 */
export function useCardJoin(eventId: string) {
  const { mutate } = useSWRConfig();
  const [pending, setPending] = useState<null | "join" | "leave">(null);
  const [failure, setFailure] = useState<JoinFailure | null>(null);

  const run = useCallback(
    async (method: "POST" | "DELETE") => {
      setFailure(null);
      setPending(method === "POST" ? "join" : "leave");
      try {
        await request<{ joinedCount: number } | undefined>(
          `/api/events/${eventId}/participants`,
          { method },
        );
        // Refresh the joined set (My events + card Join/Leave) and this event's
        // detail (its participant count), and let a re-fetched browse list settle
        // the count everywhere else. Joining also clears interest server-side, so
        // the interested set moves too.
        void mutate(joinedKey(), undefined, { revalidate: true });
        void mutate(interestedKey(), undefined, { revalidate: true });
        void mutate(detailKey(eventId), undefined, { revalidate: true });
        void mutate(eventsKey(), undefined, { revalidate: true });
        return { ok: true } as const;
      } catch (error) {
        setFailure(error instanceof ApiError ? error.joinFailure : "network");
        return { ok: false } as const;
      } finally {
        setPending(null);
      }
    },
    [eventId, mutate],
  );

  const join = useCallback(() => run("POST"), [run]);
  const leave = useCallback(() => run("DELETE"), [run]);

  return { join, leave, failure, isJoining: pending === "join", isLeaving: pending === "leave" };
}
