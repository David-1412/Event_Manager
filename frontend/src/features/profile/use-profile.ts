import useSWR from "swr";
import {
  profileActivityKey,
  profileApi,
  profileEventsKey,
  profileJoinedKey,
  profileSummaryKey,
  profileUpcomingKey,
} from "./profile-api";

/**
 * Hooks for the public profile.
 *
 * `revalidateOnFocus: false` matches the account page: a profile is not a document
 * someone is co-editing, and refetching five endpoints because a reader tabbed away
 * and back is noise.
 *
 * A null key (no id) means "not asking", which is how SWR expresses a disabled
 * request; the page resolves its id from the route before rendering these.
 */
const options = { revalidateOnFocus: false } as const;

export function useProfileSummary(id: string, viewerId?: string | null) {
  const { data, error, isLoading, mutate } = useSWR(
    id ? profileSummaryKey(id, viewerId) : null,
    // Built from the same key the subscription above is registered under, and from the
    // same `viewerId`, so the read and `useFollowProfile`'s write address one slot. The
    // viewer is a cache-partition label only: `profileApi.summary` puts it in the key and
    // nowhere in the request, so the server still decides who is asking from the bearer
    // token and never from anything the client typed.
    () => profileApi.summary(id, viewerId),
    options,
  );
  return { summary: data, error, isLoading, retry: () => void mutate() };
}

export function useProfileEvents(id: string) {
  const { data, error, isLoading, mutate } = useSWR(
    id ? profileEventsKey(id) : null,
    () => profileApi.events(id),
    options,
  );
  return { events: data, error, isLoading, retry: () => void mutate() };
}

export function useProfileUpcoming(id: string) {
  const { data, error, isLoading, mutate } = useSWR(
    id ? profileUpcomingKey(id) : null,
    () => profileApi.upcoming(id),
    options,
  );
  return { events: data, error, isLoading, retry: () => void mutate() };
}

export function useProfileJoined(id: string) {
  const { data, error, isLoading, mutate } = useSWR(
    id ? profileJoinedKey(id) : null,
    () => profileApi.joined(id),
    options,
  );
  return { events: data, error, isLoading, retry: () => void mutate() };
}

export function useProfileActivity(id: string) {
  const { data, error, isLoading, mutate } = useSWR(
    id ? profileActivityKey(id) : null,
    () => profileApi.activity(id),
    options,
  );
  return { activity: data, error, isLoading, retry: () => void mutate() };
}
