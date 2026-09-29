/**
 * The set of event ids the signed-in user has marked "Interested", persisted in
 * localStorage and namespaced per Firebase uid.
 *
 * Chosen over a backend column because there is no `interests` table and the
 * identity arrangement is a single demo user, so a server-side list would be the
 * same for every browser. Keying on the uid means two people on one machine keep
 * separate lists; the trade-off is that interests live per browser and are not
 * shared across devices.
 *
 * A module store read through `useSyncExternalStore` (the same shape as
 * `lib/query.ts`): writes are rare, so a whole-key snapshot is cheap and every
 * mounted card re-renders on change without prop threading.
 */

const STORAGE_PREFIX = "sportmeet:interested:";

/** Listeners fired after any in-tab write; the cross-tab `storage` event covers the rest. */
const listeners = new Set<() => void>();

/** The uid the store is currently scoped to; `null` until identity is known. */
let currentUid: string | null = null;

/** Whole-key snapshots, cached per raw value so getSnapshot stays referentially stable. */
let rawValue = "";
let cached: string[] = [];

function keyFor(uid: string | null): string | null {
  return uid ? `${STORAGE_PREFIX}${uid}` : null;
}

function read(uid: string | null): string[] {
  const key = keyFor(uid);
  if (!key || typeof window === "undefined") return [];
  try {
    const parsed: unknown = JSON.parse(window.localStorage.getItem(key) ?? "[]");
    return Array.isArray(parsed) ? parsed.filter((x): x is string => typeof x === "string") : [];
  } catch {
    // Quota exceeded, private mode, or corrupt JSON: an empty list degrades to
    // "nothing is interested", which is honest rather than a crash.
    return [];
  }
}

/** Re-derive the snapshot from storage; only re-emit when the raw text changed. */
function refresh(): void {
  const next = read(currentUid);
  const nextRaw = next.join(",");
  if (nextRaw !== rawValue) {
    rawValue = nextRaw;
    cached = next;
  }
}

/** Point the store at a signed-in uid (or clear it) and re-read that person's list. */
export function setInterestedScope(uid: string | null): void {
  currentUid = uid;
  rawValue = "";
  cached = [];
  refresh();
  emit();
}

export function subscribeInterest(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

/** Whole-key snapshot for `useSyncExternalStore` — stable unless the set changed. */
export function getInterestSnapshot(): string[] {
  return cached;
}

/** Whether one id is in the current user's interested list. O(n) over a short list. */
export function isInterested(id: string): boolean {
  return cached.includes(id);
}

/** Toggle membership; returns the new state so the caller can toast accordingly. */
export function toggleInterest(id: string): boolean {
  const key = keyFor(currentUid);
  if (!key) return false;
  const current = read(currentUid);
  const next = current.includes(id) ? current.filter((x) => x !== id) : [...current, id];
  try {
    window.localStorage.setItem(key, JSON.stringify(next));
  } catch {
    // Persist failed (quota / private mode). Reflect the change in-memory for this
    // tab rather than silently dropping the tap.
  }
  refresh();
  emit();
  return next.includes(id);
}

function emit(): void {
  for (const listener of listeners) listener();
}

if (typeof window !== "undefined") {
  // Sync the in-tab copy when another tab writes the same key, so a tap in one tab
  // shows up in the other without a reload.
  window.addEventListener("storage", (event) => {
    if (event.key === null || event.key.startsWith(STORAGE_PREFIX)) refresh();
  });
}
