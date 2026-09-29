"use client";

import { useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { toast } from "@/components/ui/toast";
import { Button } from "@/components/ui/button";
import { useAuth } from "@/lib/auth/auth-provider";
import { ApiError, usingFixtures } from "@/lib/api";
import { rejectDraft as rejectDraftRequest } from "@/lib/drafts";
import { removeDraft, refreshDraftQueue, useDraftQueue } from "@/features/create/use-drafts";
import { DraftRow } from "@/components/event/drafts-section";
import { ConfirmDialog } from "@/components/ui/dialog";
import type { EventDraft } from "@/types/events";

/**
 * The signed-in user's pending-draft queue: list, edit, approve, reject, delete
 * — every action scoped to the owner server-side (401 when signed out, 404 for
 * a draft that is not yours). A convenience surface beside the "Pending drafts"
 * section under /create; each row opens the create form with the draft loaded,
 * Approve publishes through that form so the create rules are the single gate,
 * and Reject/Delete act in place.
 */
export function DraftsPage() {
  const router = useRouter();
  const { user, loading } = useAuth();
  const uid = user?.uid ?? null;
  const { drafts, isLoading, totalCount } = useDraftQueue();

  const [busy, setBusy] = useState(false);
  const [pendingDelete, setPendingDelete] = useState<EventDraft | null>(null);
  const [rejecting, setRejecting] = useState<EventDraft | null>(null);
  const [note, setNote] = useState("");

  async function approve(draft: EventDraft) {
    setBusy(true);
    try {
      // Publishing is create-form approval: hand the reviewer to the form with the
      // draft loaded so the final fields (and the human decision) happen there.
      refreshDraftQueue(uid);
      router.push(`/create?draftId=${draft.id}`);
    } finally {
      setBusy(false);
    }
  }

  async function confirmReject() {
    if (!rejecting) return;
    setBusy(true);
    try {
      if (!usingFixtures) await rejectDraftRequest(rejecting.id, note || "Rejected by reviewer");
      refreshDraftQueue(uid);
      toast("Draft rejected");
    } catch (err) {
      toast(err instanceof ApiError ? err.detail ?? "Could not reject the draft." : "Could not reject the draft.");
    } finally {
      setBusy(false);
      setRejecting(null);
      setNote("");
    }
  }

  async function confirmDelete() {
    if (!pendingDelete) return;
    setBusy(true);
    const failure = await removeDraft(pendingDelete.id, uid);
    setBusy(false);
    setPendingDelete(null);
    if (failure) toast(failure);
    else toast("Draft deleted");
  }

  // Wait for the session before claiming "you have no drafts": the queue is
  // per-user, and rendering an empty one to a still-restoring session tells a
  // lie about their own data.
  if (loading || isLoading) {
    return (
      <main className="mx-auto flex w-full max-w-[720px] flex-col gap-4 px-4 py-6">
        <h1 className="text-h1 text-fg">Pending drafts</h1>
        <ul className="flex flex-col gap-2" aria-busy="true">
          {[0, 1].map((i) => (
            <li key={i} className="h-20 animate-pulse rounded-md bg-surface-2" />
          ))}
        </ul>
      </main>
    );
  }

  if (!user) {
    return (
      <main className="mx-auto flex w-full max-w-[720px] flex-col gap-4 px-4 py-6">
        <h1 className="text-h1 text-fg">Pending drafts</h1>
        <p className="rounded-md border border-dashed border-border px-4 py-8 text-center text-meta text-fg-muted">
          {"Sign in to see the drafts waiting in your queue. "}
          <Link href="/login?next=%2Fdrafts" className="text-brand-600 underline">
            Sign in
          </Link>
        </p>
      </main>
    );
  }

  return (
    <main className="mx-auto flex w-full max-w-[720px] flex-col gap-4 px-4 py-6">
      <div className="flex items-baseline justify-between gap-3">
        <h1 className="text-h1 text-fg">Pending drafts</h1>
        {totalCount > 0 && <span className="text-meta text-fg-muted">{totalCount} awaiting review</span>}
      </div>
      <p className="text-meta text-fg-muted">
        Your private queue — only you can see, edit, approve or delete these. Low confidence or a
        missing field is a hint to finish during review, never a blocker. Approve to publish (you
        become the host); Reject to keep it out.
      </p>

      {drafts.length === 0 ? (
        <p className="rounded-md border border-dashed border-border px-4 py-8 text-center text-meta text-fg-muted">
          No drafts yet.{" "}
          <Link href="/create" className="text-brand-600 underline">
            Paste a message on the create page
          </Link>{" "}
          to create one.
        </p>
      ) : (
        <ul className="flex flex-col gap-2">
          {drafts.map((draft) => (
            <li key={draft.id}>
              <DraftRow
                draft={draft}
                onRequestDelete={() => setPendingDelete(draft)}
                extraActions={
                  <div className="flex shrink-0 items-center gap-2">
                    <Button variant="ghost" size="sm" onClick={() => setRejecting(draft)}>
                      Reject
                    </Button>
                    <Button size="sm" onClick={() => void approve(draft)}>
                      Approve
                    </Button>
                  </div>
                }
              />
            </li>
          ))}
        </ul>
      )}

      <ConfirmDialog
        open={pendingDelete !== null}
        titleId="drafts-delete-title"
        title="Delete draft"
        body={pendingDelete ? <span>Delete <strong>{pendingDelete.payload.title || "this draft"}</strong>?</span> : null}
        confirmLabel="Delete"
        busy={busy}
        onConfirm={() => void confirmDelete()}
        onClose={() => setPendingDelete(null)}
      />

      <ConfirmDialog
        open={rejecting !== null}
        titleId="drafts-reject-title"
        title="Reject draft"
        body={
          <div className="flex flex-col gap-2">
            <span>Add a short note (optional). This keeps the draft out of the feed.</span>
            <textarea
              className="h-20 w-full rounded-md border border-border bg-surface p-2 text-meta text-fg"
              value={note}
              maxLength={200}
              onChange={(e) => setNote(e.target.value)}
            />
          </div>
        }
        confirmLabel="Reject draft"
        busy={busy}
        onConfirm={() => void confirmReject()}
        onClose={() => setRejecting(null)}
      />
    </main>
  );
}
