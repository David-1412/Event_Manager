import { API_BASE_URL, ApiError, usingFixtures } from "@/lib/api";
import { getAccessToken } from "@/lib/auth/token-store";
import { fixtureAddDraft } from "@/lib/fixtures";
import type { ExtractNowResponse } from "@/types/events";

/**
 * Run pasted text through the ingestion pipeline.
 *
 * With the API configured, POST /api/ingestion/extract (non-dry-run) persists a
 * Pending draft and returns the extraction result. Under fixtures there is no
 * backend to run ingestion, so a local draft is added from the pasted subject so the
 * create -> drafts -> review flow stays exercisable offline (Vitest, Playwright, dev
 * without keys) — the same "mirror the contract, don't replace it" rule as the rest
 * of the fixtures layer.
 */
export async function runIngestion(input: {
  subject: string;
  body: string;
}): Promise<ExtractNowResponse> {
  if (usingFixtures) {
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
      dryRun: false,
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
