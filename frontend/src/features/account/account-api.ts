import { usingFixtures } from "@/lib/api";
import { fetcher } from "@/lib/swr-fetcher";
import { fixtureDetail, fixtureList } from "@/lib/fixtures";
import { detailKey, hostedKey, interestedKey, joinedKey } from "@/features/events/use-events";
import type { AuthUser } from "@/lib/auth/types";
import type { EventDetail, EventListItem } from "@/types/events";
import type { UserProfile, UserStats } from "@/types/account";

/**
 * The Account page's data layer.
 *
 * TODO(backend): `GET /api/users/me` should return `UserProfile` (including
 * `memberSince` from `users.created_at`) and `PUT /api/users/me` should accept
 * `{ displayName }`. Until they exist the profile comes from the signed-in
 * identity and renaming goes through the Firebase profile (`useAuth()
 * .updateDisplayName`), which is why there is no `updateDisplayName` here.
 */
export async function getProfile(user: AuthUser): Promise<UserProfile> {
  return {
    displayName: user.displayName,
    email: user.email,
    emailVerified: user.emailVerified,
    photoURL: user.photoURL,
    memberSince: user.createdAt ?? null,
  };
}

/** Cap on detail lookups for joined events, matching `useMyEvents`' slot count. */
const MAX_JOINED_LOOKUPS = 30;

/**
 * TODO(backend): replace the body with `GET /api/users/me/stats` returning
 * `UserStats`. Today it is assembled from the endpoints My events already uses:
 * hosted events (`/me/hosting`), joined ids (`/me/joined`, resolved through the
 * detail endpoint to learn start times) and the browser-local Interested list.
 */
export async function getUserStats(): Promise<UserStats> {
  let interestedEvents = 0;

  let hosted: EventListItem[];
  let joined: EventListItem[];
  if (usingFixtures) {
    hosted = fixtureList().filter((e) => fixtureDetail(e.id)?.isHost);
    joined = fixtureList().filter((e) => fixtureDetail(e.id)?.isJoined);
    // Offline there is no server interested set; count the fixtures flagged
    // isInterested so the stat is at least self-consistent with the card.
    interestedEvents = fixtureList().filter((e) => fixtureDetail(e.id)?.isInterested).length;
  } else {
    const [hostedList, joinedIds, interestedIds] = await Promise.all([
      fetcher<EventListItem[]>(hostedKey()),
      fetcher<{ eventIds: string[] }>(joinedKey()),
      fetcher<{ eventIds: string[] }>(interestedKey()),
    ]);
    interestedEvents = interestedIds.eventIds.length;
    hosted = hostedList;
    const hostedIds = new Set(hostedList.map((e) => e.id));
    const lookups = joinedIds.eventIds.filter((id) => !hostedIds.has(id)).slice(0, MAX_JOINED_LOOKUPS);
    const details = await Promise.allSettled(lookups.map((id) => fetcher<EventDetail>(detailKey(id))));
    joined = details.flatMap((d) => (d.status === "fulfilled" ? [d.value] : []));
  }

  const now = Date.now();
  const upcoming = new Set<string>();
  for (const e of [...hosted, ...joined]) {
    if (e.status === "Scheduled" && new Date(e.startAt).getTime() > now) upcoming.add(e.id);
  }

  return { eventsCreated: hosted.length, interestedEvents, upcomingEvents: upcoming.size };
}
