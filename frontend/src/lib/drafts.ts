import { request } from "./api";
import type { CreateEventPayload } from "@/features/create/create-event-schema";
import type {
  EventDetail,
  ExtractNowRequest,
  ExtractNowResponse,
  EventDraft,
} from "@/types/events";

/**
 * Client for the review queue. Mirrors the real backend (EventDraftsController):
 * list returns a **bare array** of the caller's drafts (owner-scoped, no paging
 * envelope); update autosaves an in-progress edit; approve creates the event and
 * closes the draft in one call; reject and delete are terminal. Every endpoint
 * is scoped to the authenticated (Firebase-verified) user — 401 means "sign in",
 * 404 covers both "not yours" and "not there".
 */

/** The reviewer's queue. `status` defaults to "pending" server-side. */
export function listMyDrafts(status = "pending", limit = 200): Promise<EventDraft[]> {
  const q = new URLSearchParams({ status, limit: String(limit) });
  return request<EventDraft[]>(`/api/event-drafts?${q.toString()}`);
}

export function getDraft(id: string): Promise<EventDraft> {
  return request<EventDraft>(`/api/event-drafts/${id}`);
}

/**
 * Autosave the reviewer's in-progress payload (PUT /api/event-drafts/{id}, body
 * `{ payload }` matching the backend's UpdateDraftDto). Deliberately unvalidated
 * on the client the way approve is not: a draft is *expected* to be incomplete,
 * and the server recomputes the missing-field hints from what it is given.
 * Returns the updated draft so the caller can reconcile its row.
 */
export function updateDraft(id: string, payload: CreateEventPayload): Promise<EventDraft> {
  return request<EventDraft>(`/api/event-drafts/${id}`, {
    method: "PUT",
    body: JSON.stringify({ payload }),
  });
}

/**
 * Approve: post the reviewer's corrected create payload (the exact object
 * `toCreateEventPayload` builds for POST /api/events) to close the draft and publish
 * the event. Returns the new event's detail (201), so the caller navigates to it.
 */
export function approveDraft(
  id: string,
  event: CreateEventPayload,
  note?: string,
): Promise<EventDetail> {
  return request<EventDetail>(`/api/event-drafts/${id}/approve`, {
    method: "POST",
    body: JSON.stringify({ event, note: note ?? null }),
  });
}

/** Reject with a one-line reason. Mirrors the backend's RejectDraftDto.Reason. */
export function rejectDraft(id: string, reason: string): Promise<EventDraft> {
  return request<EventDraft>(`/api/event-drafts/${id}/reject`, {
    method: "POST",
    body: JSON.stringify({ reason }),
  });
}

/** Soft-delete: a Pending draft leaves the owner's queue (Draft -> Deleted). */
export function deleteDraft(id: string): Promise<void> {
  return request<void>(`/api/event-drafts/${id}/delete`, { method: "POST" });
}

/** Run pasted text through ingestion and persist a Pending draft for it. */
export function extractNow(input: ExtractNowRequest): Promise<ExtractNowResponse> {
  return request<ExtractNowResponse>(`/api/ingestion/extract`, {
    method: "POST",
    body: JSON.stringify(input),
  });
}

