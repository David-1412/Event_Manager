"use client";

import { useState } from "react";
import Link from "next/link";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { ConfirmDialog } from "@/components/ui/dialog";
import { toast } from "@/components/ui/toast";
import { useAuth } from "@/lib/auth/auth-provider";
import { removeDraft, useDraftQueue } from "@/features/create/use-drafts";
import type { EventDraft } from "@/types/events";

/** Human label for each missing create-form field the extractor flagged. Keyed
 * by the backend's CreateEventDto property names (the API's missingFields
 * vocabulary); the create form maps them onto its own inputs' labels. */
const FIELD_LABELS: Record<string, string> = {
  title: "name",
  startAt: "start time",
  endAt: "end time",
  timezone: "timezone",
  venueName: "venue",
  address: "address",
  maxParticipants: "spots",
  cost: "cost",
  description: "description",
  tags: "tags",
  skillLevel: "skill level",
};

function confidenceTone(confidence: number | null): "brand" | "warn" | "neutral" {
  if (confidence == null) return "neutral";
  if (confidence >= 0.8) return "brand";
  return "warn";
}

function formatConfidence(confidence: number | null): string {
  if (confidence == null) return "heuristic";
  return `${Math.round(confidence * 100)}%`;
}

/**
 * The drafts a pasted email or Discord message produced, under the create form
 * (EMAIL_INGESTION_PLAN §7). Each row is a Pending proposal: the extractor's
 * confidence and the fields it could not fill are shown to guide review, never to
 * block — the row is always openable so the human can complete and publish it.
 */
export function DraftsSection() {
  const { user } = useAuth();
  const uid = user?.uid ?? null;
  const { drafts, isLoading, totalCount } = useDraftQueue();

  const [pendingDelete, setPendingDelete] = useState<EventDraft | null>(null);
  const [busy, setBusy] = useState(false);

  async function confirmDelete() {
    if (!pendingDelete) return;
    setBusy(true);
    const failure = await removeDraft(pendingDelete.id, uid);
    setBusy(false);
    setPendingDelete(null);
    if (failure) toast(failure);
    else toast("Draft deleted");
  }

  return (
    <section aria-labelledby="drafts-heading" className="flex flex-col gap-3">
      <div className="flex items-baseline justify-between gap-3">
        <h2 id="drafts-heading" className="text-h3 text-fg">
          Pending drafts
        </h2>
        {totalCount > 0 && (
          <span className="text-meta text-fg-muted" aria-live="polite">
            {totalCount} awaiting review
          </span>
        )}
      </div>
      <p className="text-meta text-fg-muted">
        Your private queue: drafts from pasted messages and from the mailbox
        poller. Open one to complete it, then publish it or save and finish
        later. Low confidence or a missing field is a hint to fill in during
        review — it never blocks the draft.
      </p>

      {isLoading ? (
        <ul className="flex flex-col gap-2" aria-busy="true">
          {[0, 1].map((i) => (
            <li key={i} className="h-20 animate-pulse rounded-md bg-surface-2" />
          ))}
        </ul>
      ) : drafts.length === 0 ? (
        <p className="rounded-md border border-dashed border-border px-4 py-6 text-center text-meta text-fg-muted">
          No drafts yet. Paste a message above to create one.
        </p>
      ) : (
        <ul className="flex flex-col gap-2">
          {drafts.map((draft) => (
            <DraftRow
              key={draft.id}
              draft={draft}
              onOpen={undefined}
              onRequestDelete={() => setPendingDelete(draft)}
            />
          ))}
        </ul>
      )}

      <ConfirmDialog
        open={pendingDelete !== null}
        titleId="draft-delete-title"
        title="Delete draft"
        body={
          pendingDelete ? (
            <span>
              Delete <strong>{pendingDelete.payload.title || "this draft"}</strong>? It leaves your
              drafts. This cannot be undone.
            </span>
          ) : null
        }
        confirmLabel="Delete"
        busy={busy}
        onConfirm={() => void confirmDelete()}
        onClose={() => setPendingDelete(null)}
      />
    </section>
  );
}

export function DraftBanner({
  title,
  confidence,
  missingFields,
}: {
  title: string;
  confidence: number | null;
  missingFields: string[];
}) {
  const missing = missingFields.map((f) => FIELD_LABELS[f] ?? f);
  return (
    <div className="flex flex-col gap-2 rounded-md border border-brand-600/40 bg-brand-tint p-3">
      <div className="flex flex-wrap items-center gap-2">
        <Badge tone="brand">Editing draft</Badge>
        <span className="text-body font-medium text-fg">{title || "Untitled draft"}</span>
        <Badge tone={confidenceTone(confidence)}>{formatConfidence(confidence)}</Badge>
      </div>
      <p className="text-meta text-fg-muted">
        {missing.length > 0
          ? `Fill these in, then publish: ${missing.join(", ")}. You can save and finish later.`
          : "Complete anything the message left out, then publish. Save and finish any time."}
      </p>
    </div>
  );
}

export function DraftRow({
  draft,
  onOpen,
  onRequestDelete,
  extraActions,
}: {
  draft: EventDraft;
  /** Provided by the drafts page for a full review view; the create-page section
   * renders the row as a link instead, so this is optional. */
  onOpen?: () => void;
  onRequestDelete: () => void;
  /** Approve/Reject, etc. Rendered beside Delete on the review page. */
  extraActions?: React.ReactNode;
}) {
  const confidence = formatConfidence(draft.confidence);
  const missing = draft.missingFields.map((f) => FIELD_LABELS[f] ?? f);

  const body = (
    <>
      <div className="flex flex-wrap items-center gap-2">
        <span className="text-body font-medium text-fg">
          {draft.payload.title || "Untitled draft"}
        </span>
        <Badge tone={confidenceTone(draft.confidence)}>{confidence}</Badge>
        {draft.duplicateOfEventId && <Badge tone="info">Possible duplicate</Badge>}
      </div>
      <p className="text-meta text-fg-muted">
        from {draft.fromAddr || "an unknown sender"}
        {draft.subject ? ` · ${draft.subject}` : ""}
      </p>
      {missing.length > 0 && (
        <p className="text-meta text-warn">Needs: {missing.join(", ")}</p>
      )}
    </>
  );

  const link = `/create?draftId=${draft.id}`;

  return (
    <li className="flex flex-col gap-2 rounded-md border border-border bg-surface p-3 sm:flex-row sm:items-center sm:justify-between">
      {onOpen ? (
        <button type="button" onClick={onOpen} className="press flex flex-1 flex-col gap-1 text-left">
          {body}
        </button>
      ) : (
        <Link href={link} className="press flex flex-1 flex-col gap-1 text-left">
          {body}
        </Link>
      )}
      <div className="flex shrink-0 items-center gap-2">
        {extraActions}
        <Button
          variant="ghost"
          size="sm"
          onClick={(event) => {
            event.preventDefault();
            event.stopPropagation();
            onRequestDelete();
          }}
        >
          Delete
        </Button>
      </div>
    </li>
  );
}

