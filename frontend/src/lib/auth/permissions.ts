import type { UserRole } from "@/types/admin";

/**
 * What each role may do — the client's copy of the backend's
 * `UserRolePermissions` table, so the UI asks for a capability ("can this person
 * publish a public event straight away?") rather than comparing role names.
 *
 * Purely a rendering hint: the API authorises every action on its own side, so a
 * stale or wrong answer here can only show or hide a control, never grant access.
 * An unknown role (still loading, or signed out) holds no permission — the safe
 * direction, since every admin surface is meant to be invisible rather than
 * merely forbidden.
 */
export interface Permissions {
  /** Public events this user creates publish immediately (Creator, Admin). */
  canPublishPublicEvents: boolean;
  /** The review queue: approve or reject other people's public events (Admin). */
  canReviewEvents: boolean;
  /** `/admin/users`: list accounts and change roles (Admin). */
  canManageUsers: boolean;
}

const table: Record<UserRole, Permissions> = {
  Member: { canPublishPublicEvents: false, canReviewEvents: false, canManageUsers: false },
  Creator: { canPublishPublicEvents: true, canReviewEvents: false, canManageUsers: false },
  Admin: { canPublishPublicEvents: true, canReviewEvents: true, canManageUsers: true },
};

const none: Permissions = table.Member;

export function permissionsFor(role: UserRole | undefined): Permissions {
  return role ? table[role] : none;
}

/** Every role, lowest privilege first — the order the role picker lists them. */
export const USER_ROLES: readonly UserRole[] = ["Member", "Creator", "Admin"];

/** Narrow an untrusted wire value to the union, or null. */
export function parseUserRole(value: unknown): UserRole | null {
  return typeof value === "string" && (USER_ROLES as readonly string[]).includes(value)
    ? (value as UserRole)
    : null;
}
