import { melbourneDateInputValue, melbourneTimeInputValue } from "@/lib/format";
import type { DraftPayload } from "@/types/events";
import type { CreateEventValues } from "./create-event-schema";

/** Map the ingestion payload onto the create-form shape. Every gap stays empty —
 * the review step is where a human fills them; missing fields never block.
 *
 * The payload stores instants (`startAt`/`endAt`); the form edits a Melbourne
 * date + HH:mm pair, which is exactly the pair `toCreateEventPayload` turns back
 * into an instant — so an unedited round trip lands on the same moment. */
export function payloadToFormValues(payload: DraftPayload): CreateEventValues {
  const start = payload.startAt ? new Date(payload.startAt) : null;
  const end = payload.endAt ? new Date(payload.endAt) : null;
  const hasStart = start && !Number.isNaN(start.getTime());
  const hasEnd = end && !Number.isNaN(end.getTime());

  return {
    title: payload.title ?? "",
    description: payload.description ?? "",
    date: hasStart ? melbourneDateInputValue(start) : "",
    startTime: hasStart ? melbourneTimeInputValue(start) : "18:00",
    endTime: hasEnd ? melbourneTimeInputValue(end) : "20:00",
    venueName: payload.venueName ?? "",
    address: payload.address ?? "",
    thumbnailUrl: payload.thumbnailUrl ?? "",
    latitude: payload.latitude ?? 0,
    longitude: payload.longitude ?? 0,
    maxParticipants: payload.maxParticipants ?? 4,
    cost: payload.cost != null ? String(payload.cost) : "",
    tags: payload.tags ?? [],
    // An ingested message never says "private", so a draft with no visibility
    // lands on the form's default rather than on an empty select.
    visibility: payload.visibility ?? "Public",
  };
}

/** A draft carries a venue name/address/coords but no Google place id, so the
 * picker reuses the three it has. Absent coordinates -> null (empty picker). */
export function payloadToVenue(payload: DraftPayload): {
  venueName: string;
  address: string;
  latitude: number;
  longitude: number;
} | null {
  if (payload.latitude == null || payload.longitude == null) return null;
  return {
    venueName: payload.venueName ?? "",
    address: payload.address ?? "",
    latitude: payload.latitude,
    longitude: payload.longitude,
  };
}

