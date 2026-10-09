"use client";

import { useRef, useState } from "react";
import { Badge, type BadgeTone } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Field, Textarea } from "@/components/ui/field";
import { ApiError } from "@/lib/api";
import { importText } from "@/features/create/import-api";
import { BAND_TEXT, confidenceBand, type ConfidenceBand } from "@/features/create/field-flags";
import type { ImportDraftResponse } from "@/types/events";

/** Below this a paste is a stray word or a link, not an invitation worth sending. */
const AUTO_FILL_MIN_CHARS = 30;
const MAX_CHARS = 12_000;

const BAND_TONE: Record<ConfidenceBand, BadgeTone> = {
  ready: "brand",
  check: "warn",
  low: "danger",
  basic: "warn",
};

/**
 * The first thing on `/create`: paste the invitation and the form below fills itself.
 * Pasting is the whole interaction. The extraction starts the moment text lands, with a
 * button for typed or edited text, because the goal is event creation in under 30 seconds
 * and every click between paste and a filled form is time against it.
 *
 * It owns no form state. It hands the result up and shows only the verdict: how far to
 * trust the fill, and how many fields are flagged for a look.
 */
export function ImportTextPanel({
  onImported,
}: {
  onImported: (result: ImportDraftResponse) => void;
}) {
  const [text, setText] = useState("");
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<ImportDraftResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const field = useRef<HTMLTextAreaElement>(null);
  // A ref as well as state: a paste handler runs before the re-render that disables it,
  // so a second paste could otherwise start a second request.
  const running = useRef(false);

  async function fill(source: string) {
    const trimmed = source.trim();
    if (!trimmed || running.current) return;
    running.current = true;
    setBusy(true);
    setError(null);
    try {
      const response = await importText(trimmed);
      setResult(response);
      if (response.kind === "extracted" && response.payload) onImported(response);
    } catch (err) {
      setResult(null);
      setError(messageFor(err));
    } finally {
      running.current = false;
      setBusy(false);
    }
  }

  return (
    <section
      aria-label="Fill from pasted text"
      className="flex flex-col gap-3 rounded-md border border-border bg-surface p-4"
    >
      <div>
        <h2 className="text-h3 text-fg">Paste the details</h2>
        <p className="mt-1 text-meta text-fg-muted">
          Paste an invitation, message or caption and we&rsquo;ll fill in the form below.
        </p>
      </div>

      <Field label="Event text" hint={`${text.length.toLocaleString()}/${MAX_CHARS.toLocaleString()}`}>
        {({ id, describedBy }) => (
          <Textarea
            ref={field}
            id={id}
            aria-describedby={describedBy}
            rows={4}
            value={text}
            onChange={(e) => setText(e.target.value.slice(0, MAX_CHARS))}
            onPaste={(e) => {
              const pasted = e.clipboardData.getData("text");
              if (pasted.trim().length < AUTO_FILL_MIN_CHARS) return;
              // The textarea has not taken the pasted text yet; read it once it has.
              setTimeout(() => void fill(field.current?.value ?? pasted), 0);
            }}
            placeholder="Badminton Friday 14 Nov 7:30pm at Seddon Park, 42 Railway Ave. $5, beginners welcome."
          />
        )}
      </Field>

      <div className="flex flex-wrap items-center gap-3">
        <Button onClick={() => void fill(text)} loading={busy} disabled={!text.trim()}>
          {busy ? "Reading…" : "Fill the form"}
        </Button>
        {text && !busy && (
          <Button
            variant="ghost"
            onClick={() => {
              setText("");
              setResult(null);
              setError(null);
            }}
          >
            Clear
          </Button>
        )}
        <div aria-live="polite" className="flex min-w-0 flex-wrap items-center gap-2 text-meta">
          {result?.kind === "extracted" && <Verdict result={result} />}
          {result?.kind === "no_event" && (
            <span className="text-fg-muted">{result.detail ?? "That doesn't look like an event."}</span>
          )}
          {error && <span role="alert" className="text-danger">{error}</span>}
        </div>
      </div>
    </section>
  );
}

function Verdict({ result }: { result: ImportDraftResponse }) {
  const band = confidenceBand(result);
  const toCheck = result.flags.length;
  return (
    <>
      <Badge tone={BAND_TONE[band]}>{BAND_TEXT[band]}</Badge>
      {toCheck > 0 && (
        <span className="text-fg-muted">
          {toCheck} {toCheck === 1 ? "field" : "fields"} to check
        </span>
      )}
    </>
  );
}

/** A sentence a person can act on. The server's own wording is already written for
 *  that (empty text, too long, a link, rate limited), so it is shown as-is. */
function messageFor(err: unknown): string {
  if (err instanceof ApiError) {
    if (err.status === 401) return "Sign in to fill the form from pasted text.";
    if (err.status === 0) return "Couldn't reach the server. Try again, or fill the form in directly.";
    return err.message;
  }
  if (err instanceof DOMException && err.name === "AbortError") {
    return "That took too long. Try again, or fill the form in directly.";
  }
  return "Couldn't read that. Try again, or fill the form in directly.";
}
