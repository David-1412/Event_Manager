"use client";

import useSWR, { mutate as globalMutate } from "swr";
import { API_BASE_URL, usingFixtures } from "@/lib/api";
import { fetcher } from "@/lib/swr-fetcher";
import { useAuth } from "@/lib/auth/auth-provider";
import { fixtureDraftQueue, fixtureDeleteDraft } from "@/lib/fixtures";
import { deleteDraft as deleteDraftRequest } from "@/lib/drafts";
import type { EventDraft } from "@/types/events";


/**
 * The signed-in user's draft queue. Keyed on the Firebase uid so the cache holds
 * each reviewer's queue apart and SWR swaps the data the moment the user changes
 * (the plan's "per-user drafts" requirement). Anonymous or fixtures mode never
 * fetches: offline dev reads fixtures, an unauthenticated caller has no queue.
 */
export const draftsKey = (uid: string | null) =>
  uid ? `${API_BASE_URL}/api/event-drafts?status=pending&uid=${uid}` : null;

export function useDraftQueue() {
  const { user } = useAuth();
  const uid = user?.uid ?? null;
  const key = usingFixtures ? null : draftsKey(uid);

  const { data, isLoading, error, mutate } = useSWR<EventDraft[]>(key, fetcher, {
    revalidateOnFocus: false,
  });

  const drafts = usingFixtures ? fixtureDraftQueue().items : data ?? [];

  return {
    drafts,
    totalCount: drafts.length,
    isLoading: usingFixtures ? false : isLoading,
    error,
    reload: mutate,
  };
}


/** After any draft mutation, refresh the queue. No-op under fixtures (the module
 * store already reflects the change and the next render re-reads it). */
export function refreshDraftQueue(uid: string | null) {
  if (usingFixtures) return;
  void globalMutate(draftsKey(uid));
}

/** Soft-delete a draft (Draft -> Deleted). Returns the API's rejection reason so the
 * caller can surface why an approve/delete failed rather than swallowing it. */
export async function removeDraft(id: string, uid: string | null): Promise<string | null> {
  if (usingFixtures) {
    fixtureDeleteDraft(id);
    return null;
  }
  try {
    await deleteDraftRequest(id);
    refreshDraftQueue(uid);
    return null;
  } catch (err) {
    return err instanceof Error ? err.message : "Could not delete the draft.";
  }
}

