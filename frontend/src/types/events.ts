/**
 * Naming contract with the backend (UIUX_DESIGN_SPEC.md §12).
 * DTO names mirror `EventListItemDto` etc. so the generated client can drop in.
 */

export type SkillLevel = "Beginner" | "Intermediate" | "Advanced";
export type EventStatus = "Scheduled" | "Cancelled" | "Completed";

/**
 * Discovery, not access. `Public` events appear in the browse feed; `Private`
 * ones never show up in `GET /api/events` and are reachable only through the
 * direct link the host shares — which is why the detail page stays open to
 * anyone who has that link. Mirrors the backend `EventVisibility` enum.
 */
export type EventVisibility = "Public" | "Private";

/**
 * Free-text tag, always normalized (lowercase, no '#') by the server. Replaces
 * the old SportKey union: the vocabulary is no longer closed, so a new tag is a
 * keystroke rather than a type change and a redeploy.
 */
export type Tag = string;

/** `GET /api/tags/popular` item: a tag and how many visible events carry it. */
export interface PopularTag {
  name: Tag;
  count: number;
}



export interface ParticipantDto {
  id: string;
  displayName: string;
  avatarUrl: string | null;
}

/** `GET /api/events` item. */
export interface EventListItem {
  id: string;
  title: string;
  tags: Tag[];
  /** Server-supplied emoji (lives in the DB seed). null when the event has no
   * sport, since sport is decoration now and not required. */
  sportIcon: string | null;
  /** null when the host chose no level; every renderer must hide the badge. */
  skillLevel: SkillLevel | null;
  startAt: string;
  endAt: string;
  timezone: string;
  venueName: string;
  address: string;
  /** Host-uploaded thumbnail URL (served from the API's /uploads/...), or null
   * when the event has no image. The card/preview/detail hide the image on null. */
  thumbnailUrl: string | null;
  latitude: number;
  longitude: number;
  /** null or 0 => renders `Free`. */
  cost: number | null;
  maxParticipants: number;
  /** Participants with a real participant row. Formerly `participantCount`;
   * renamed to pair with `interestedCount` and match the API's `joinedCount`. */
  joinedCount: number;
  /** Events marked Interested (a softer signal than joined; reserves no spot). */
  interestedCount: number;
  status: EventStatus;
  isCancelled: boolean;
  /** "Public" events are in the browse feed; "Private" ones only through their
   * link. The detail page offers a copyable link when this is "Private". */
  visibility: EventVisibility;
  distanceKm: number | null;
}

/** `GET /api/events/{id}`. */
export interface EventDetail extends EventListItem {
  description: string | null;
  host: ParticipantDto;
  isHost: boolean;
  isJoined: boolean;
  isInterested: boolean;
  participants: ParticipantDto[];
  cancelledAt: string | null;
}

export type EventSort = "startAt" | "distance";

export type EventDateFilter = "today" | "week" | "any";

export interface EventQuery {
  q: string | null;
  /** Normalized tag name; null means no tag filter. Replaces the sport filter. */
  tag: Tag | null;
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

/**
 * Event drafts (EMAIL_INGESTION_PLAN §7.5 / M2). A draft is a pasted email or
 * Discord message the ingestion pipeline turned into a candidate event; it is a
 * Pending proposal that a human reviews and completes before it can be published.
 *
 * `confidence` and `missingFields` guide the reviewer's attention — they
 * never block draft creation. A draft with low confidence or many missing fields is
 * still a valid draft; it just needs more work before it becomes an event.
 */
export type DraftStatus = "pending" | "approved" | "rejected" | "duplicate" | "deleted";

/**
 * The event fields an ingestion source supplied, in the shape the backend's
 * `CreateEventDto` stores them (and the `/create` form consumes after mapping).
 * Every field is optional: an incomplete extraction leaves gaps, and the review
 * step is where they get filled. `tags` are names (the create form resolves them
 * to Tag objects on submit). Times are ISO instants, never wall-clock strings —
 * `use-draft-to-form` converts them to the form's Melbourne date/time pair.
 */
export interface DraftPayload {
  title?: string;
  description?: string;
  /** ISO instant; backend CreateEventDto.StartAt (DateTimeOffset). */
  startAt?: string;
  /** ISO instant; backend CreateEventDto.EndAt (DateTimeOffset). */
  endAt?: string;
  timezone?: string;
  venueName?: string;
  address?: string;
  /** Uploaded thumbnail URL, carried through the draft so it survives review and
   * lands on the approved event. Absent means no image. */
  thumbnailUrl?: string | null;
  latitude?: number;
  longitude?: number;
  maxParticipants?: number;
  cost?: number;
  tags?: string[];
  /** Backend SkillLevel enum string, or null when the event has no level. */
  skillLevel?: string | null;
  /** Backend EventVisibility enum string. Absent means Public on approve. */
  visibility?: EventVisibility;
}

/**
 * Mirrors backend `EventDraftDto`. The list endpoint returns these as a **bare
 * array** (no paging envelope); confidence/source/provenance come flattened from
 * the joined email row, so `confidence` is a plain number and the source lives in
 * `subject` / `fromAddr` / `sentAt` / `bodyText` — there is no nested source object.
 * `eventId` is set once the draft is approved (the event it produced).
 */
export interface EventDraft {
  id: string;
  /** The owner. Always the signed-in caller for anything the list/detail return
   * (every read is owner-scoped), so the client can key per-user state on it. */
  userId: string;
  status: DraftStatus;
  payload: DraftPayload;
  confidence: number;
  missingFields: string[];
  // Source message (flattened from the joined IngestedEmail).
  subject: string;
  fromAddr: string;
  sentAt: string | null;
  bodyText: string;
  // Extraction provenance.
  model?: string | null;
  promptVersion?: string | null;
  latencyMs?: number | null;
  rawExtraction?: string | null;
  createdAt: string;
  // Review outcome.
  eventId: string | null;
  duplicateOfEventId: string | null;
  reviewNote: string | null;
  reviewedAt: string | null;
}

/** A row of the current user's draft queue — the shape `useDraftQueue` hands the
 * review UI. Identical to `EventDraft` today; the alias names the role the page
 * renders and keeps fixtures/callers honest about what "queue item" means. */
export type DraftQueueItem = EventDraft;

/** The queue envelope the fixture store returns (the live API answers with the
 * bare array `EventDraft[]`; this mirrors that after the fixture's local sort). */
export interface DraftQueue {
  items: DraftQueueItem[];
  totalCount: number;
}


/** POST /api/ingestion/extract request — body-only paste path (no IMAP). */
export interface ExtractNowRequest {
  subject?: string;
  fromAddr?: string;
  body: string;
  dryRun?: boolean;
}

/** POST /api/ingestion/extract response. `kind` is "created" | "no_event" |
 *  "duplicate" | "error"; `draftId` is set on a real (non-dry-run) save. */
export interface ExtractNowResponse {
  kind: string;
  confidence: number | null;
  missingFields: string[];
  payload: DraftPayload | null;
  draftId: string | null;
  detail?: string | null;
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
