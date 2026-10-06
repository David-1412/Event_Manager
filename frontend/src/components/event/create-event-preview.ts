import type { EventListItem } from "@/types/events";
import type { VenueSelection } from "@/components/event/venue-picker";
import type { CreateEventValues } from "@/features/create/create-event-schema";
import { normalizeTags } from "@/lib/sports";

/** Fields can arrive `undefined` while `useWatch` hydrates, so every read here
 *  is total - the preview must never render `undefined`, `NaN` or an empty date
 *  (spec §13 forbids exactly those). */
type Draft = Partial<CreateEventValues>;

/** The preview needs an `EventListItem`; synthesize one from the draft. */
export function previewOf(values: Draft, venue: VenueSelection | null): EventListItem {
  const spots = Number(values.maxParticipants);
  const cost = Number(values.cost);
  return {
    id: "preview",
    title: (values.title ?? "").trim() || "Your event name",
    tags: normalizeTags(values.tags ?? []),
    // null, not a guessed glyph: the draft has no sport row, and inventing one is
    // what made the old preview show 🏸 on an event the user never picked.
    sportIcon: null,
    skillLevel: null,

    startAt: toIso(values.date, values.startTime),
    endAt: toIso(values.date, values.endTime),
    timezone: "Australia/Melbourne",
    venueName: venue?.venueName || "Venue not chosen yet",
    address: venue?.address ?? "",
    // The uploaded thumbnail URL straight from the form, so the live preview shows
    // the image the moment it finishes uploading. null (nothing uploaded) is the
    // same state the card renders for an event with no image.
    thumbnailUrl: values.thumbnailUrl?.trim() ? values.thumbnailUrl.trim() : null,
    latitude: venue?.latitude ?? 0,
    longitude: venue?.longitude ?? 0,
    cost: Number.isFinite(cost) && cost > 0 ? cost : null,
    maxParticipants: Number.isFinite(spots) && spots >= 2 ? spots : 4,
    joinedCount: 1,
    interestedCount: 0,
    status: "Scheduled",
    isCancelled: false,
    // Mirrors the form's choice so the preview is honest about what will be
    // created. The card itself does not render it; the type requires it.
    visibility: values.visibility === "Private" ? "Private" : "Public",
    distanceKm: null,
  };
}

function toIso(date: string | undefined, time: string | undefined): string {
  if (!date || !time) return new Date().toISOString();
  const parsed = new Date(`${date}T${time}`);
  return Number.isNaN(parsed.getTime()) ? new Date().toISOString() : parsed.toISOString();
}
