import { mutate as globalMutate } from "swr";
import { request } from "@/lib/api";

/**
 * A host's lifecycle actions on their own event: cancel, reopen, and - once it is
 * cancelled - delete. Shared by My Events and the event page so both call the same
 * endpoints and refresh the same caches.
 */

export const setEventStatus = (eventId: string, action: "cancel" | "reopen") =>
  request<void>(`/api/events/${eventId}/${action}`, { method: "PATCH" });

/** Cancelled events only: the API refuses (422) while the event is still live. */
export const deleteCancelledEvent = (eventId: string) =>
  request<void>(`/api/events/${eventId}`, { method: "DELETE" });

/** Every cached events read - browse pages of any filter, My Events, the detail page -
 *  so a cancelled or deleted event is not left showing anywhere. */
export const refreshEventCaches = () =>
  globalMutate((key) => typeof key === "string" && key.includes("/api/events"));

export const DELETE_EVENT_CONFIRM =
  "Delete this event? It will be removed from your events and its link will stop working. This can't be undone.";
