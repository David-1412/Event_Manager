import type { EventListItem } from "./events";
import type { UserRole } from "./admin";

/**
 * The public profile at `/profile/[id]` — what a visitor may learn about a host.
 *
 * The shape is deliberately narrow. These types are the client's view of an anonymous
 * endpoint, so a field absent here is a field the app has no way to render, which is
 * the useful direction for a privacy boundary to be biased in. Nothing in this file
 * carries an email address, an analytics number, an event-view count, or any signal
 * about participation in a private event.
 */

/** How a profile presents itself. `"Person"` for every profile today; `"Club"` is
 * reserved so an organization profile can arrive as data rather than a redesign.
 * Nothing branches on it yet — see the note on the backend `ProfileKind`. */
export type ProfileKind = "Person" | "Club";

/** The four Overview numbers. Server-computed, unlike the account page's identical
 * tiles which are still assembled client-side (see features/account/account-api.ts).
 * `joinedEvents` counts public events only. */
export interface ProfileStats {
  eventsCreated: number;
  upcomingEvents: number;
  interestedEvents: number;
  joinedEvents: number;
}

/** `GET /api/profiles/{id}`. */
export interface ProfileSummary {
  userId: string;
  displayName: string;
  avatarUrl: string | null;
  /** The backend role name. Render through `RoleBadge`, which maps Moderator to the
   * "Creator" label — never compare against a label in component code. */
  role: UserRole | null;
  bio: string | null;
  organizationName: string | null;
  profileKind: ProfileKind;
  /** ISO timestamp; rendered as "member since". */
  memberSince: string;
  stats: ProfileStats;
  /** How many people follow this profile. Public, and shown for every visitor
   * including signed-out ones. */
  followersCount: number;
  /** Whether the *caller* follows this profile. `false` for a signed-out visitor,
   * where it means "no identity", not "decided not to" — which is why the follow
   * control renders only once the session has resolved. */
  isFollowing: boolean;
}

/** `POST`/`DELETE /api/profiles/{id}/follow`. Both verbs return the resulting state
 * rather than an echo of the request, so a retried tap settles on the server's answer
 * instead of flipping a local boolean twice. */
export interface ProfileFollowResult {
  isFollowing: boolean;
  followersCount: number;
}

/** The activity verbs, mirroring the backend `ProfileActivityKind`. */
export type ProfileActivityKind = "Created" | "Hosted" | "Joined" | "Interested";

/** One row of the activity feed, newest first. */
export interface ProfileActivity {
  kind: ProfileActivityKind;
  eventId: string;
  eventTitle: string;
  occurredAt: string;
}

/** The event lists (`/events`, `/upcoming`, `/joined`) reuse `EventListItem` whole,
 * so a profile's tiles are the same card component with the same fields as browse
 * rather than a second shape that can drift from it. */
export type ProfileEventList = EventListItem[];
