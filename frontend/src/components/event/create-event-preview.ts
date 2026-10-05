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
    latitude: venue?.latitude ?? 0,
    longitude: venue?.longitude ?? 0,
    cost: Number.isFinite(cost) && cost > 0 ? cost : null,
    maxParticipants: Number.isFinite(spots) && spots >= 2 ? spots : 4,
    participantCount: 1,
    status: "Scheduled",
    isCancelled: false,
    distanceKm: null,
  };
}

function toIso(date: string | undefined, time: string | undefined): string {
  if (!date || !time) return new Date().toISOString();
  const parsed = new Date(`${date}T${time}`);
  return Number.isNaN(parsed.getTime()) ? new Date().toISOString() : parsed.toISOString();
}
