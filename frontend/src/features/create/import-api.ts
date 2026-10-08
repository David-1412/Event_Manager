import { request, usingFixtures } from "@/lib/api";
import type { CreatePath, ImportDraftResponse } from "@/types/events";

/** An AI extraction plus a geocode can legitimately take a few seconds; the default
 *  10 s ceiling is for ordinary reads. The server gives up on the model at 30 s. */
const IMPORT_TIMEOUT_MS = 25_000;

/**
 * Turn pasted text into a pre-filled event. Needs a signed-in user (the create page
 * already does), and throws `ApiError` with a person-facing message for the cases the
 * user can fix: empty or oversized text, a lone link, rate limiting.
 */
export async function importText(text: string): Promise<ImportDraftResponse> {
  if (usingFixtures) return fixtureImport(text);
  return request<ImportDraftResponse>("/api/imports/text", {
    method: "POST",
    body: { text },
    timeoutMs: IMPORT_TIMEOUT_MS,
  });
}

/**
 * Report how long publishing took, and which route the user took, so the median can be
 * measured against the manual baseline. Fire-and-forget on purpose: a metrics failure
 * must never be something the person who just published an event has to see.
 */
export function reportPublish(input: {
  eventId: string;
  durationMs: number;
  path: CreatePath;
  importId: string | null;
}): void {
  if (usingFixtures) return;
  void request("/api/publish-metrics", { method: "POST", body: input }).catch(() => undefined);
}

/** Offline stand-in so the create flow stays exercisable without a backend: the first
 *  line becomes the title and everything else is left for the user, flagged. */
function fixtureImport(text: string): ImportDraftResponse {
  const title = text.split("\n")[0]?.trim().slice(0, 120) ?? "";
  return {
    importId: "fixture-import",
    kind: "extracted",
    confidence: 0.6,
    missingFields: ["startAt", "endAt", "venueName"],
    payload: { title },
    flags: [
      { field: "startAt", reason: "missing" },
      { field: "endAt", reason: "assumed" },
      { field: "venue", reason: "missing" },
      { field: "maxParticipants", reason: "assumed" },
    ],
    geocode: null,
    basic: true,
    detail: null,
  };
}
