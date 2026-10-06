import { API_BASE_URL, ApiError, usingFixtures } from "@/lib/api";
import { getAccessToken } from "@/lib/auth/token-store";
import { fixtureAddDraft } from "@/lib/fixtures";
import type { ExtractNowResponse } from "@/types/events";

/**
 * Run pasted text through the ingestion pipeline.
 *
 * `dryRun` (the create page's paste panel) extracts without persisting anything:
 * the backend runs the extractor and returns the proposal payload with no
 * `draftId`, so the caller can drop it straight into the create form. The
 * non-dry-run path (POST /api/ingestion/extract) persists a Pending draft and is
 * what the review queue's own flow uses. Under fixtures there is no backend to run
 * ingestion, so the dry run mirrors the contract with a local proposal built from
 * the pasted subject (no draft added), while a real run adds a local draft so the
 * create -> drafts -> review flow stays exercisable offline (Vitest, Playwright, dev
 * without keys) — the same "mirror the contract, don't replace it" rule as the rest
 * of the fixtures layer.
 */
export async function runIngestion(input: {
  subject: string;
  body: string;
  dryRun?: boolean;
}): Promise<ExtractNowResponse> {
  if (usingFixtures) {
    if (input.dryRun) {
      return {
        kind: "extracted",
        confidence: 0.6,
        missingFields: input.subject ? ["startAt", "endAt"] : ["title", "startAt", "endAt"],
        payload: { title: input.subject || "" },
        draftId: null,
        detail: null,
      };
    }
    const draft = fixtureAddDraft({ title: input.subject || undefined });
    return {
      kind: "created",
      confidence: draft.confidence,
      missingFields: draft.missingFields,
      payload: draft.payload,
      draftId: draft.id,
      detail: null,
    };
  }

  const token = getAccessToken();
  const response = await fetch(`${API_BASE_URL}/api/ingestion/extract`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Accept: "application/json",
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: JSON.stringify({
      subject: input.subject || undefined,
      body: input.body,
      dryRun: input.dryRun ?? false,
    }),
  });

  const text = await response.text();
  let payload: unknown = null;
  if (text) {
    try {
      payload = JSON.parse(text);
    } catch {
      payload = null;
    }
  }

  if (!response.ok) {
    const detail =
      payload && typeof payload === "object" && "detail" in payload
        ? String((payload as { detail?: unknown }).detail)
        : `Ingestion failed (${response.status})`;
    throw new ApiError({
      code: "Unknown",
      status: response.status,
      title: response.statusText || "Ingestion failed",
      detail,
    });
  }

  return payload as ExtractNowResponse;
}
