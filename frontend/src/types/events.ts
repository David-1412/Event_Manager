import type { UserRole } from "./admin";

/**
 * Naming contract with the backend (UIUX_DESIGN_SPEC.md §12).
 * DTO names mirror `EventListItemDto` etc. so the generated client can drop in.
 */

export type SkillLevel = "Beginner" | "Intermediate" | "Advanced";
/**
 * Mirrors the backend `EventStatus` enum. `Scheduled` and `Published` are both
 * live (visible in Browse, joinable) — the review workflow introduced `Published`
 * as the approved twin of the pre-existing `Scheduled`, so never test for one
 * alone. `Draft`, `PendingReview` and `Rejected` are never publicly visible;
 * `PendingReview` is what a regular user's public event becomes until an
 * administrator decides, which is the state My Events has to surface.
 */
export type EventStatus =
  | "Scheduled"
  | "Cancelled"
  | "Completed"
  | "Draft"
  | "PendingReview"
  | "Published"
  | "Rejected";

/** True when the status means the event is live in Browse. Mirrors the backend's
 * `EventStatusExtensions.IsPublished`, so the two sides cannot drift apart. */
export function isPublishedStatus(status: EventStatus): boolean {
  return status === "Scheduled" || status === "Published";
}

/** Awaiting an administrator's decision. Kept separate from `isPublishedStatus`
 * because the UI needs to talk about this one state specifically: it drives the
 * Pending Approval badge, the awaiting-review notice, and the submit dialog. */
export function isPendingReviewStatus(status: EventStatus): boolean {
  return status === "PendingReview";
}

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
  /** The full description. The browse preview panel quotes the opening of it, so
   *  it ships with the list; optional because responses written before the field
   *  existed lack it, and every renderer must treat absent as "no preview text". */
  description?: string | null;
  tags: Tag[];
  /** Server-supplied emoji (lives in the DB seed). null when the event has no
   * sport, since sport is decoration now and not required. */
  sportIcon: string | null;
  /** The host's display name. Not rendered by the browse card; the admin review
   * queue needs it, since a decision about someone else's public event has to say
   * whose event it is. Absent from responses that predate the field, hence optional. */
  hostName?: string | null;
  /** Host photo URL; null renders the initials avatar. */
  hostPhotoUrl?: string | null;
  /** The host's id — what turns the name and avatar into a link to their public
   * profile. Optional because responses written before the field existed, and every
   * fixture, lack it; `HostLink` renders plain text rather than a dead link. */
  hostId?: string | null;
  /** The host's role, driving the Creator badge. Optional for the same reason as
   * `hostId`; null/absent renders no badge rather than guessing "Member". */
  hostRole?: UserRole | null;
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
  /** Capacity, or `null` for no limit — the host left Spots empty. Never collapse
   * null to 0: the renderers show `No limit` and the event cannot fill. */
  maxParticipants: number | null;
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
  /** Whether joining needs the host's approval. Optional because responses written
   *  before the field existed lack it, and absent must mean the ordinary
   *  immediate-join flow — never "assume gated". */
  requiresJoinApproval?: boolean;
  /** One session of a repeating series (each session is its own event). Optional
   *  for the same reason as requiresJoinApproval: absent means a one-off. */
  isRecurring?: boolean;
  /** The first session's id, shared by the whole series. */
  seriesId?: string | null;
  /** "Weekly on Tue, Thu · until 15 Dec 2026", or null for a one-off. */
  recurrenceSummary?: string | null;
  distanceKm: number | null;
}

/** The lifecycle of a request to join an approval-gated event. */
export type JoinRequestStatus = "Pending" | "Approved" | "Rejected";

/** `GET /api/events/{id}/join-requests` item. */
export interface JoinRequest {
  requester: ParticipantDto;
  status: JoinRequestStatus;
  requestedAt: string;
  decidedAt: string | null;
}

/** `GET /api/events/{id}/join-requests`. */
export interface JoinRequestList {
  items: JoinRequest[];
  pendingCount: number;
}

/**
 * `GET /api/events/{id}/analytics` — the host's Event Insights.
 *
 * Host-only: the endpoint 404s for anyone who is not `event.host.id`, which is why
 * no other response carries `views`. `joinedCount` and `interestedCount` are the
 * same numbers the public card already shows, returned together so the panel is one
 * request rather than re-deriving counts already on the page.
 */
export interface EventAnalytics {
  views: number;
  joinedCount: number;
  interestedCount: number;
}


/** `GET /api/events/{id}`. */
export interface EventDetail extends EventListItem {
  host: ParticipantDto;
  /** The host's role, driving the "Hosted by" badge. The detail DTO carries the host
   * as a `ParticipantDto` (which has no role) plus `HostRole` from the list item it
   * extends, so the detail page reads the role here rather than off `host`. Optional
   * because responses predating the field lack it, which renders no badge. */
  hostRole?: UserRole | null;
  isHost: boolean;
  isJoined: boolean;
  isInterested: boolean;
  participants: ParticipantDto[];
  cancelledAt: string | null;
  /** The viewer's own standing in the approval queue, null when they never asked.
   *  "Pending" is the state the detail page renders as Pending approval — distinct
   *  from isJoined, which only becomes true once the host says yes. */
  myJoinRequestStatus?: JoinRequestStatus | null;
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
  /**
   * The browse tab. "all" is the ordinary feed; "following" narrows it to events
   * hosted by creators the signed-in viewer follows and is served by a different
   * endpoint (`/api/events/me/following`), which is why `buildEventsKey` never
   * emits it — the tab selects the route, the remaining fields are the filters
   * that route honours. "Upcoming" and "Nearby" are not tabs: they are the
   * existing `date`/`sort` filters, which the Following feed applies too.
   */
  tab: BrowseTab;
}

/** The browse feed selector. URL value `?tab=following`; absent means "all". */
export type BrowseTab = "all" | "following";

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
  /** Absent or null means no capacity limit. */
  maxParticipants?: number | null;
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


/** Why the import flagged a field. The client maps each to a sentence. */
export type ImportFlagReason = "missing" | "assumed" | "unclear" | "past" | "unconfirmed";

/** Fields the import can flag; `venue` covers the name, address and map pin. */
export type ImportFlagField = "title" | "startAt" | "endAt" | "venue" | "maxParticipants";

export interface ImportFlag {
  field: ImportFlagField;
  reason: ImportFlagReason;
}

/** POST /api/imports/text. `payload` is the body the create form posts. `basic` means
 *  the heuristic extractor answered, which cannot read a venue or address. */
export interface ImportDraftResponse {
  importId: string;
  kind: "extracted" | "no_event";
  confidence: number | null;
  missingFields: string[];
  payload: DraftPayload | null;
  flags: ImportFlag[];
  geocode: { locationType: string; needsConfirm: boolean } | null;
  basic: boolean;
  detail: string | null;
}

/** Which route a published event took, for the create-time metric. */
export type CreatePath = "manual" | "import" | "draft";

/** Problem Details (RFC 9457) minus the noise we don't render. */
export interface ProblemDetails {
  status: number;
  title: string;
  detail?: string;
  code?:
    | "EventFull"
    | "ValidationError"
    | "NotFound"
    | "ImportEmptyInput"
    | "ImportTooLong"
    | "ImportUrlNotSupported"
    | "ImportBusy"
    | "ImportRateLimited"
    | "Unknown";
  errors?: Record<string, string[]>;
}

/** Every failure path JoinButton distinguishes (spec §7). */
export type JoinFailure = "full" | "network" | "server";
