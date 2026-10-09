import type { ImportDraftResponse, ImportFlag, ImportFlagField, ImportFlagReason } from "@/types/events";
import type { CreateEventValues } from "./create-event-schema";
import type { VenueSelection } from "@/components/event/venue-picker";

/**
 * The "check this" markers on the create form.
 *
 * The server decides which fields to flag and why; this decides how that reads and
 * when it goes away. A flag clears on its own the moment the user changes the field it
 * points at (the thing it asked them to look at has been looked at), or when they say
 * "Looks right". It never blocks Publish.
 */

const TEXT: Record<ImportFlagReason, string> = {
  missing: "Not found in your text",
  assumed: "Assumed. We didn't find this",
  unclear: "Check this. It wasn't stated clearly",
  past: "This date has already passed",
  unconfirmed: "Confirm the location on the map",
};

export const flagText = (reason: ImportFlagReason): string => TEXT[reason];

/** A flag plus the field value it was raised against. */
export interface RaisedFlag {
  reason: ImportFlagReason;
  baseline: string;
}

export type RaisedFlags = Partial<Record<ImportFlagField, RaisedFlag>>;

/** What the flagged field currently holds, as one comparable string. */
export function fieldSnapshot(
  field: ImportFlagField,
  values: CreateEventValues,
  venue: VenueSelection | null,
): string {
  switch (field) {
    case "title":
      return values.title ?? "";
    case "startAt":
      return `${values.date}|${values.startTime}`;
    case "endAt":
      return values.endTime ?? "";
    case "venue":
      return venue ? `${venue.latitude},${venue.longitude}` : "";
    case "maxParticipants":
      return String(values.maxParticipants ?? "");
  }
}

/** Record the server's flags against the values the form was just filled with. */
export function raiseFlags(
  flags: ImportFlag[],
  values: CreateEventValues,
  venue: VenueSelection | null,
): RaisedFlags {
  const raised: RaisedFlags = {};
  for (const flag of flags) {
    raised[flag.field] = {
      reason: flag.reason,
      baseline: fieldSnapshot(flag.field, values, venue),
    };
  }
  return raised;
}

/** The flags still worth showing: unedited and not dismissed. */
export function activeFlags(
  raised: RaisedFlags,
  dismissed: ReadonlySet<ImportFlagField>,
  values: CreateEventValues,
  venue: VenueSelection | null,
): Partial<Record<ImportFlagField, ImportFlagReason>> {
  const active: Partial<Record<ImportFlagField, ImportFlagReason>> = {};
  for (const [field, flag] of Object.entries(raised) as [ImportFlagField, RaisedFlag][]) {
    if (dismissed.has(field)) continue;
    if (fieldSnapshot(field, values, venue) !== flag.baseline) continue;
    active[field] = flag.reason;
  }
  return active;
}

export type ConfidenceBand = "ready" | "check" | "low" | "basic";

/**
 * One word for how much to trust the fill. Banded rather than a raw percentage because
 * a model's self-reported 0.83 is not a measurement, and "Ready to review" is something
 * a person can act on. A basic (heuristic) answer is its own band: it never read the
 * venue or address, whatever its number says.
 */
export function confidenceBand(result: Pick<ImportDraftResponse, "confidence" | "basic">): ConfidenceBand {
  if (result.basic) return "basic";
  const c = result.confidence ?? 0;
  return c >= 0.8 ? "ready" : c >= 0.5 ? "check" : "low";
}

export const BAND_TEXT: Record<ConfidenceBand, string> = {
  ready: "Ready to review",
  check: "Check the highlighted fields",
  low: "We couldn't read much. Fill in the rest",
  basic: "Basic reading. Venue and address aren't detected",
};
