"use client";

import useSWR from "swr";
import { API_BASE_URL, usingFixtures } from "@/lib/api";
import { useAuth } from "@/lib/auth/auth-provider";
import { fetcher } from "@/lib/swr-fetcher";
import type { EventSession } from "@/types/events";

/** Keyed per viewer (in a `#` fragment `fetch` never sends) because `isJoined` is. */
export const sessionsKey = (eventId: string, viewerId?: string | null) =>
  `${API_BASE_URL}/api/events/${eventId}/sessions${viewerId ? `#viewer=${viewerId}` : ""}`;

/**
 * The upcoming sessions of the series `eventId` belongs to, earliest first.
 * `enabled` lets the card's drawer fetch only once it is opened, so a browse page
 * of series cards does not fire one request per card.
 */
export function useEventSessions(eventId: string, enabled = true) {
  const { user, loading } = useAuth();
  const key = enabled && !usingFixtures && !loading ? sessionsKey(eventId, user?.id) : null;
  const { data, error, isLoading } = useSWR<EventSession[]>(key, fetcher);
  return { sessions: data, error, isLoading: Boolean(key) && isLoading };
}

/** "Fri 23 Oct" in the event's own timezone. */
export function sessionDay(iso: string, timeZone: string): string {
  // Built from parts: en-AU's own pattern is "Fri, 23 Oct", with a comma.
  const parts = new Intl.DateTimeFormat("en-AU", { weekday: "short", day: "numeric", month: "short", timeZone })
    .formatToParts(new Date(iso));
  const part = (type: string) => parts.find((p) => p.type === type)?.value ?? "";
  return `${part("weekday")} ${part("day")} ${part("month")}`;
}

/** "6:00 pm" in the event's own timezone. */
export function sessionTime(iso: string, timeZone: string): string {
  return new Date(iso).toLocaleTimeString("en-AU", { hour: "numeric", minute: "2-digit", timeZone });
}

/** "Repeats weekly" / "Repeats fortnightly" / ... for a series' frequency. */
export function repeatsLabel(frequency: string | null | undefined): string {
  switch (frequency) {
    case "DAILY":
      return "Repeats daily";
    case "BIWEEKLY":
      return "Repeats fortnightly";
    case "MONTHLY":
      return "Repeats monthly";
    case "WEEKLY":
      return "Repeats weekly";
    default:
      return "Repeats";
  }
}
