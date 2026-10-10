import { request } from "@/lib/api";
import type { NotificationList } from "@/types/notifications";

export function listNotifications(): Promise<NotificationList> {
  return request<NotificationList>("/api/notifications");
}

export function markNotificationRead(id: string): Promise<void> {
  return request<void>(`/api/notifications/${id}/read`, { method: "PATCH" });
}

export function markAllNotificationsRead(): Promise<void> {
  return request<void>("/api/notifications/read", { method: "PATCH" });
}
