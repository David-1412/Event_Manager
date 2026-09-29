"use client";

import { useCallback, useEffect, useMemo, useSyncExternalStore } from "react";
import { useAuth } from "@/lib/auth/auth-provider";
import {
  getInterestSnapshot,
  setInterestedScope,
  subscribeInterest,
  toggleInterest,
} from "./interest-store";

export { setInterestedScope } from "./interest-store";

const EMPTY: string[] = [];

/**
 * The signed-in user's Interested set, backed by `interest-store`.
 *
 * Interest is per-browser (localStorage keyed on the Firebase uid), so scope
 * follows `useAuth().user?.uid`: signing in or out re-points the store at the
 * right list. The snapshot is the whole set; single-id membership is checked
 * against it (a short list, so `includes` beats a selector re-read per card).
 */
export function useInterests() {
  const { user } = useAuth();
  const uid = user?.uid ?? null;

  // Scope the store to the current person. A null uid (signed out) clears it, so
  // interest is never attributed to whoever used the browser before.
  useEffect(() => {
    setInterestedScope(uid);
  }, [uid]);

  const snapshot = useSyncExternalStore(subscribeInterest, getInterestSnapshot, () => EMPTY);

  // The store re-emits whenever its scope resets (each mount's scope effect
  // calls setInterestedScope, which clears its cache), so the raw snapshot can
  // change identity with the same contents. Normalize to a stable reference by
  // membership, otherwise every consumer memo (useMyEvents' wanted -> items ->
  // slots) recreates per render and the slot effect loop cascades.
  const joinedKey = snapshot.join(",");
  const ids = useMemo(
    () => (joinedKey === "" ? EMPTY : joinedKey.split(",")),
    [joinedKey],
  );

  const isInterested = useCallback((id: string) => ids.includes(id), [ids]);
  const toggle = useCallback((id: string) => toggleInterest(id), []);

  return { ids, isInterested, toggle };
}
