/**
 * Naming contract with the backend (UIUX_DESIGN_SPEC.md §12).
 * DTO names mirror `EventListItemDto` etc. so the generated client can drop in.
 */

export type SkillLevel = "Beginner" | "Intermediate" | "Advanced";

export type SportKey =
  | "badminton"
  | "basketball"
  | "running"
  | "soccer"
  | "tennis"
  | "cricket"
  | "netball"
  | "volleyball";

export interface ParticipantDto {
  id: string;
  displayName: string;
  avatarUrl: string | null;
}

/** `GET /api/events` item. */
export interface EventListItem {
  id: string;
  title: string;
  sport: SportKey;
  /** Server-supplied emoji (lives in the DB seed). */
  sportIcon: string | null;
  skillLevel: SkillLevel;
  startAt: string;
  endAt: string;
  timezone: string;
  venueName: string;
  address: string;
  latitude: number;
  longitude: number;
  /** null or 0 => renders `Free`. */
  cost: number | null;
  maxParticipants: number;
  participantCount: number;
  isCancelled: boolean;
  distanceKm: number | null;
}

/** `GET /api/events/{id}`. */
export interface EventDetail extends EventListItem {
  description: string | null;
  host: ParticipantDto;
  isHost: boolean;
  isJoined: boolean;
  participants: ParticipantDto[];
  cancelledAt: string | null;
}

export type EventSort = "startAt" | "distance";

export type EventDateFilter = "today" | "week" | "any";

export interface EventQuery {
  q: string | null;
  sport: SportKey | null;
  date: EventDateFilter;
  radiusKm: number | null;
  sort: EventSort;
  page: number;
}

/** Envelope the API returns for collections. */
export interface Paged<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

/** Problem Details (RFC 9457) minus the noise we don't render. */
export interface ProblemDetails {
  status: number;
  title: string;
  detail?: string;
  code?: "EventFull" | "ValidationError" | "NotFound" | "Unknown";
  errors?: Record<string, string[]>;
}

/** Every failure path JoinButton distinguishes (spec §7). */
export type JoinFailure = "full" | "network" | "server";
