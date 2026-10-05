"use client";

import { useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Field, Input, Textarea } from "@/components/ui/field";
import { toast } from "@/components/ui/toast";
import { useAuth } from "@/lib/auth/auth-provider";
import { refreshDraftQueue } from "@/features/create/use-drafts";
import { runIngestion } from "@/features/create/ingest";
import type { ExtractNowResponse } from "@/types/events";

/**
 * The paste-to-draft surface (EMAIL_INGESTION_PLAN §7). A user drops in an email or
 * a Discord message; the backend runs it through ingestion (LLM extractor with the
 * heuristic as fallback) and creates a Pending draft. Extraction confidence and the
 * missing fields are shown as review guidance only — a low-confidence or incomplete
 * extract still produces a draft, which is the whole point of drafts: a human fills
 * the gaps before publishing.
 */
export function DraftsPastePanel({
  onSaved,
  onExtracted,
}: {
  onSaved?: () => void;
  onExtracted?: (result: ExtractNowResponse) => void;
}) {
  const { user } = useAuth();
  const uid = user?.uid ?? null;

  const [subject, setSubject] = useState("");
  const [body, setBody] = useState("");
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<{
    kind: string;
    confidence: number | null;
    missingFields: string[];
  } | null>(null);

  async function createDraft() {
    const text = body.trim();
    if (!text) {
      toast("Paste an email or Discord message first");
      return;
    }
    setBusy(true);
    setResult(null);
    try {
      const payload = await runIngestion({ subject: subject.trim(), body: text });
      setResult({
        kind: payload.kind,
        confidence: payload.confidence,
        missingFields: payload.missingFields ?? [],
      });
      if (payload.payload) onExtracted?.(payload);
      if (payload.draftId) {
        toast("Draft created");
        refreshDraftQueue(uid);
        onSaved?.();
      } else if (payload.kind === "no_event") {
        toast("No event found in that message");
      } else {
        toast("Draft created");
      }
    } catch (err) {
      toast(err instanceof Error ? err.message : "Could not run ingestion.");
    } finally {
      setBusy(false);
    }
  }


  return (
    <div className="flex flex-col gap-4 rounded-md border border-border bg-surface p-4">
      <div>
        <h3 className="text-h3 text-fg">Create a draft from a message</h3>
        <p className="mt-1 text-meta text-fg-muted">
          Paste the email or Discord message. The AI extracts what it can; anything missing becomes
          a hint you complete during review.
        </p>
      </div>

      <Field label="Subject / title" optional>
        {({ id }) => (
          <Input
            id={id}
            value={subject}
            onChange={(e) => setSubject(e.target.value)}
            placeholder="Friday Social Badminton"
            maxLength={200}
          />
        )}
      </Field>

      <Field label="Message body" hint={`${body.length}/12000`}>
        {({ id }) => (
          <Textarea
            id={id}
            rows={7}
            value={body}
            onChange={(e) => setBody(e.target.value.slice(0, 12000))}
            placeholder="Badminton at Burwood East courts, Friday 8pm, $7 a head, all levels. Bring a racket…"
          />
        )}
      </Field>

      <div className="flex flex-wrap items-center justify-between gap-3">
        <Button onClick={createDraft} loading={busy}>
          {busy ? "Extracting…" : "Create draft"}
        </Button>
        {result && (
          <div className="flex items-center gap-2 text-meta text-fg-muted" aria-live="polite">
            <Badge tone={result.confidence == null ? "neutral" : result.confidence >= 0.8 ? "brand" : "warn"}>
              {result.confidence == null ? "heuristic" : `${Math.round(result.confidence * 100)}% confident`}
            </Badge>
            {result.missingFields.length > 0 && (
              <span>missing {result.missingFields.length} field{result.missingFields.length > 1 ? "s" : ""}</span>
            )}
          </div>
        )}
      </div>
    </div>
  );
}
