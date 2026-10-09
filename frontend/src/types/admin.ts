import type { EventListItem } from "./events";

/**
 * Types for the administrator surface. Kept out of `types/events.ts` because
 * nothing here describes an event: these are accounts and privileges, and the
 * admin pages are the only consumers.
 *
 * `UserRole` mirrors the backend's `SportMeet.Domain.Enums.UserRole` serialized by
 * `UserRoleJsonConverter`, i.e. the PascalCase CLR name — not the numeric ordinal.
 * A client that typed this as `number` would compile and render every badge wrong.
 */
export type UserRole = "Member" | "Admin";

/** One row of the admin user table. */
export interface AdminUser {
  /** The API's `users.id`, which is what promote/demote key on. */
  id: string;
  name: string;
  /** Null for accounts whose sign-in token carried no email claim. */
  email: string | null;
  role: UserRole;
  /** ISO timestamp of when the account first appeared. */
  createdAt: string;
}

/** `GET /api/admin/users`. */
export interface AdminUserList {
  items: AdminUser[];
  /** Admins across the whole table, not just this page — the client needs the true
   * count to decide whether a demote would hit the last-admin floor. */
  adminCount: number;
  totalCount: number;
}

/** `POST /api/admin/users/{id}/promote` and `/demote`. */
export interface RoleChangeResult {
  /** The user as they now are, so the row can be patched in place. */
  user: AdminUser;
  /** Admins remaining after the change. */
  adminCount: number;
}

/** A public event awaiting approval — `GET /api/events/reviews/pending`.
 *
 * Deliberately the *same* type the browse feed renders: the backend builds the
 * queue from `v_event_feed` through `ToListItem`, so a queue row is literally the
 * card its creator sees. Reviewing therefore happens against the real thing rather
 * than a second approximation of it. `hostName` is what makes the queue usable —
 * an administrator approving a stranger's public event has to be able to see whose
 * event it is. */
export type PendingEvent = EventListItem;
