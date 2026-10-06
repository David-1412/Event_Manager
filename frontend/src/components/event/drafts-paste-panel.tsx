"use client";

import { useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Field, Input, Textarea } from "@/components/ui/field";
import { toast } from "@/components/ui/toast";
import { runIngestion } from "@/features/create/ingest";
import type { ExtractNowResponse } from "@/types/events";

/**
 * The paste-to-form surface (EMAIL_INGESTION_PLAN §7). A user drops in an email or
 * a Discord message; the backend runs it through the extractor (LLM with the
 * heuristic as fallback) as a **dry run** — nothing is saved as a draft. The
 * extracted fields are handed to `onExtracted` so the create form fills in below,
 * and the reviewer completes the gaps before publishing. Extraction confidence and
 * the missing fields are shown as review guidance only — a low-confidence or
 * incomplete extract still fills what it could, which is the whole point: a human
 * finishes the job in the form rather than a half-extracted draft landing in a queue.
 */
export function DraftsPastePanel({
  onExtracted,
}: {
  onExtracted?: (result: ExtractNowResponse) => void;
}) {
  const [subject, setSubject] = useState("");
  const [body, setBody] = useState("");
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<{
    kind: string;
    confidence: number | null;
    missingFields: string[];
  } | null>(null);

  async function extract() {
    const text = body.trim();
    if (!text) {
      toast("Paste an email or Discord message first");
      return;
    }
    setBusy(true);
    setResult(null);
    try {
      const payload = await runIngestion({
        subject: subject.trim(),
        body: text,
        dryRun: true,
      });
      setResult({
        kind: payload.kind,
        confidence: payload.confidence,
        missingFields: payload.missingFields ?? [],
      });
      if (payload.payload) {
        onExtracted?.(payload);
        toast("Filled the form from that message", "success");
      } else if (payload.kind === "no_event") {
        toast("No event found in that message");
      } else {
        toast("Nothing to fill from that message");
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
          Paste the email or Discord message. The AI extracts what it can and fills
          the form below; anything missing stays a hint for you to complete.
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
        <Button onClick={extract} loading={busy}>
          {busy ? "Extracting…" : "Extract"}
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
