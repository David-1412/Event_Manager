import { request } from "./api";
import type { AdminUserList, PendingEvent, RoleChangeResult, UserRole } from "@/types/admin";

/**
 * Client for the administrator surface.
 *
 * Everything here is Admin-only server-side. A signed-in Member does **not** get
 * 403 — `RequireAdminAttribute` answers 404 so the admin surface is not
 * discoverable from the outside, which is also why the UI hides the nav link rather
 * than showing a page that errors: the client should never learn the difference.
 *
 * The review queue lives on the events controller (`/api/events/reviews/*`), not
 * under `/api/admin/*`, because those endpoints already enforce Admin through the
 * service layer and their contract predates this page. Grouping by resource rather
 * than by permission is the backend's existing choice; this file just follows it.
 */

/** Every account, newest first, plus the true admin count. */
export function listAdminUsers(): Promise<AdminUserList> {
  return request<AdminUserList>("/api/admin/users");
}

/**
 * Promote a Member to Admin. Idempotent server-side (an existing Admin comes back
 * unchanged and nothing is written). 404 covers "no such user" and "you are not an
 * Admin" alike.
 */
export function promoteUser(id: string): Promise<RoleChangeResult> {
  return request<RoleChangeResult>(`/api/admin/users/${id}/promote`, { method: "POST" });
}

/**
 * Demote an Admin to Member. Refuses with 422 + a human sentence when this is the
 * last Admin account — `ApiError.detail` carries it, and the caller shows it rather
 * than inventing its own wording for a rule it cannot evaluate (the count is the
 * server's).
 */
export function demoteUser(id: string): Promise<RoleChangeResult> {
  return request<RoleChangeResult>(`/api/admin/users/${id}/demote`, { method: "POST" });
}

/** Set a user's role to Member, Moderator or Admin. */
export function setUserRole(id: string, role: UserRole): Promise<RoleChangeResult> {
  return request<RoleChangeResult>(`/api/admin/users/${id}/role`, {
    method: "PUT",
    body: { role },
  });
}

/** Public events submitted by regular users and awaiting a decision. */
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

/** Mirrors `DemoEngagementResultDto`: what one press added or removed. */
export interface DemoEngagementResult {
  demoAccounts: number;
  events: number;
  posts: number;
  views: number;
  interests: number;
}

/**
 * Give every live event and forum post 1-9 anonymous views and 1-3 Interested
 * marks from the hidden placeholder accounts demo1..demo20. Each call adds more.
 * Set-based on the server but touches every row, so it gets a longer timeout.
 */
export function addDemoEngagement(): Promise<DemoEngagementResult> {
  return request<DemoEngagementResult>("/api/admin/users/demo-engagement", { method: "POST", timeoutMs: 60_000 });
}

/** Remove everything the demo tool ever added, restoring the real counts. */
export function removeDemoEngagement(): Promise<DemoEngagementResult> {
  return request<DemoEngagementResult>("/api/admin/users/demo-engagement", { method: "DELETE", timeoutMs: 60_000 });
}
