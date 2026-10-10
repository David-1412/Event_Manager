import { request } from "@/lib/api";
import type {
  AdminEventPage,
  AdminEventTimeFrame,
  UpdateAdminEvent,
} from "@/types/admin-events";
import type { EventStatus } from "@/types/events";

export function listAdminEvents(options: {
  q: string;
  timeFrame: AdminEventTimeFrame | null;
  status: EventStatus | null;
  page: number;
  pageSize: number;
}): Promise<AdminEventPage> {
  const params = new URLSearchParams({
    page: String(options.page),
    pageSize: String(options.pageSize),
  });
  if (options.q) params.set("q", options.q);
  if (options.timeFrame) params.set("timeFrame", options.timeFrame);
  if (options.status) params.set("status", options.status);
  return request<AdminEventPage>(`/api/admin/events?${params.toString()}`);
}

export function updateAdminEvent(id: string, event: UpdateAdminEvent) {
  return request(`/api/admin/events/${id}`, { method: "PUT", body: event });
}

export function deleteAdminEvent(id: string): Promise<void> {
  return request<void>(`/api/admin/events/${id}`, { method: "DELETE" });
}
