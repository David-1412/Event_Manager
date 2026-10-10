import { request } from "./api";
import type { AdminUserList, PendingEvent, RoleChangeResult, UserRole } from "@/types/admin";

/**
 * Client for the administrator surface.
 *
 * Everything here is Admin-only server-side. A signed-in Member or Creator does
 * **not** get 403 — `RequirePermissionAttribute` answers 404 so the admin surface is not
 * discoverable from the outside, which is also why the UI hides the nav link rather
 * than showing a page that errors: the client should never learn the difference.
 *
 * The review queue lives on the events controller (`/api/events/reviews/*`), not
 * under `/api/admin/*`, because those endpoints already enforce the `ReviewEvents`
 * permission and their contract predates this page. Grouping by resource rather
 * than by permission is the backend's existing choice; this file just follows it.
 */

/** Every account, newest first, plus the true admin count. */
export function listAdminUsers(): Promise<AdminUserList> {
  return request<AdminUserList>("/api/admin/users");
}

/**
 * Move an account to Member, Creator or Admin. Idempotent server-side (asking for
 * the role they already hold returns them unchanged and writes nothing). 404 covers
 * "no such user" and "you are not an Admin" alike.
 *
 * Refuses with 422 + a human sentence when this would take away the last Admin —
 * `ApiError.detail` carries it, and the caller shows it rather than inventing its
 * own wording for a rule it cannot evaluate (the count is the server's).
 */
export function setUserRole(id: string, role: UserRole): Promise<RoleChangeResult> {
  return request<RoleChangeResult>(`/api/admin/users/${id}/role`, {
    method: "POST",
    body: { role },
  });
}

/** Public events submitted by Members and awaiting a decision. */
export function listPendingEvents(): Promise<PendingEvent[]> {
  return request<PendingEvent[]>("/api/events/reviews/pending");
}

/** Publish a pending event. It enters the public feed immediately. */
export function approveEvent(id: string): Promise<void> {
  return request<void>(`/api/events/${id}/approve`, { method: "POST" });
}

/** Decline a pending event. It stays with its creator, who can edit and resubmit. */
export function rejectEvent(id: string): Promise<void> {
  return request<void>(`/api/events/${id}/reject`, { method: "POST" });
}
