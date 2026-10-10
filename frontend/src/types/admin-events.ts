import type { EventStatus, EventVisibility, SkillLevel } from "@/types/events";

export interface AdminEvent {
  id: string;
  title: string;
  hostId: string;
  hostName: string;
  description: string | null;
  venueName: string;
  address: string | null;
  thumbnailUrl: string | null;
  latitude: number;
  longitude: number;
  timezone: string;
  startAt: string;
  endAt: string;
  maxParticipants: number;
  skillLevel: SkillLevel | null;
  cost: number | null;
  status: EventStatus;
  visibility: EventVisibility;
  tags: string[];
}

export interface AdminEventPage {
  items: AdminEvent[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export type UpdateAdminEvent = Omit<AdminEvent, "id" | "hostId" | "hostName">;

export type AdminEventTimeFrame = "Past" | "Current" | "Future";
