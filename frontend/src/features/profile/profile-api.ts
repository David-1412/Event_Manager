import { API_BASE_URL } from "@/lib/api";
import { fetcher } from "@/lib/swr-fetcher";
import type { ProfileActivity, ProfileEventList, ProfileSummary } from "@/types/profile";

/**
 * Data layer for the public profile.
 *
 * Four endpoints rather than one, because the sections have genuinely different
 * freshness and cost: the summary is needed for first paint, while the three event
 * lists and the feed are below the fold and can arrive on their own. One endpoint
 * returning all five payloads would make the fastest possible first paint wait on the
 * slowest query on the page.
 *
 * Unlike features/account/account-api.ts there is no fixture branch here. That file
 * assembles its numbers client-side because the backend endpoints it wants were never
 * written (they are still marked TODO); these ones exist, so offline mode shows the
 * profile's own error state rather than inventing a stranger's activity, which for a
 * page about *other people* is the honest fallback.
 */

export const profileSummaryKey = (id: string, viewerId?: string | null) =>
  // Keyed on the viewer, unlike every other key in this file. `isFollowing` is a fact
  // about whoever is asking, so an anonymous key would hand one signed-out reader's
  // answer to the next person to sign in on the same browser — and after a sign-out it
  // would keep showing the previous account's follow state. The id is only ever a
  // cache-partition label, never an input to the request: the server takes the caller
  // from the bearer token, which is what keeps this from becoming a way to ask about a
  // stranger.
  //
  // A `#` fragment rather than a `?viewer=` query, because the key doubles as the URL
  // every other reader in this file is handed and `fetcher` passes it to `fetch`
  // untouched. A fragment is not sent to the server, so the partition stays exactly
  // that — a partition — instead of an unbound parameter on a real request. It also
  // keeps the partitioned and unpartitioned forms of this key visually distinct.
  //
  // The three event lists and the activity feed stay unkeyed because they genuinely do
  // not vary by caller.
  `${API_BASE_URL}/api/profiles/${id}${viewerId ? `#viewer=${viewerId}` : ""}`;
export const profileEventsKey = (id: string) => `${API_BASE_URL}/api/profiles/${id}/events`;
export const profileUpcomingKey = (id: string) => `${API_BASE_URL}/api/profiles/${id}/upcoming`;
export const profileJoinedKey = (id: string) => `${API_BASE_URL}/api/profiles/${id}/joined`;
export const profileActivityKey = (id: string) => `${API_BASE_URL}/api/profiles/${id}/activity`;

export const profileApi = {
  // Takes the viewer even though the viewer never reaches the network, because the key
  // this builds has to be byte-identical to the one `useProfileSummary` subscribes to and
  // the one `useFollowProfile` writes. It was not: the read asked for the unpartitioned
  // key while the write updated `?viewer=<id>`, so a successful follow landed in a cache
  // slot nobody was subscribed to and the button kept reading the stale copy until a full
  // reload. The count moved because that came back on the next fetch; the button did not,
  // because nothing told the reader's slot anything had changed.
  summary: (id: string, viewerId?: string | null) =>
    fetcher<ProfileSummary>(profileSummaryKey(id, viewerId)),

  events: (id: string) => fetcher<ProfileEventList>(profileEventsKey(id)),
  upcoming: (id: string) => fetcher<ProfileEventList>(profileUpcomingKey(id)),
  joined: (id: string) => fetcher<ProfileEventList>(profileJoinedKey(id)),
  activity: (id: string) => fetcher<ProfileActivity[]>(profileActivityKey(id)),
};
