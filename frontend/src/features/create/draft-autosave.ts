/**
 * Local autosave for the draft editor (the "restore edits when revisiting, warn
 * before navigation if unsaved" contract of the review flow).
 *
 * localStorage, not the server: the browser tab can die between keystrokes, and
 * the reviewer's most recent intent must survive that even if the last PUT
 * never left the machine. The server copy (PUT /api/event-drafts/{id}) remains
 * the source of truth across *devices*; this is the same-device safety net, and
 * the two are reconciled at open time by timestamp (newest wins).
 *
 * Keyed per draft id, and cleared on approve/reject/delete/publish — a restored
 * edit for a draft that is no longer pending would silently reopen a decided
 * decision.
 */

const PREFIX = "sportmeet:draft-edit:";

export interface LocalDraftEdit {
  /** JSON.stringify of the create-form values, stored opaquely so this module
   * knows nothing about the form's shape. */
  values: string;
  /** Wall-clock the edit was written; compared against the server draft's
   * updatedAt-ish timestamps to decide which copy the editor opens with. */
  savedAt: string;
}

function storage(): Storage | null {
  try {
    return typeof window === "undefined" ? null : window.localStorage;
  } catch {
    // Private-browsing Safari throws on access; autosave degrades to absent.
    return null;
  }
}

export function saveLocalDraftEdit(id: string, values: unknown): void {
  const store = storage();
  if (!store) return;
  const edit: LocalDraftEdit = {
    values: JSON.stringify(values),
    savedAt: new Date().toISOString(),
  };
  try {
    store.setItem(PREFIX + id, JSON.stringify(edit));
  } catch {
    // Quota full or storage disabled: the PUT to the API is still saving.
  }
}

export function loadLocalDraftEdit(id: string): LocalDraftEdit | null {
  const store = storage();
  if (!store) return null;
  try {
    const raw = store.getItem(PREFIX + id);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as LocalDraftEdit;
    return typeof parsed.values === "string" && typeof parsed.savedAt === "string"
      ? parsed
      : null;
  } catch {
    return null;
  }
}

/** Parse a stored edit back into form values; null when unreadable. */
export function parseLocalDraftEdit<T>(edit: LocalDraftEdit | null): T | null {
  if (!edit) return null;
  try {
    return JSON.parse(edit.values) as T;
  } catch {
    return null;
  }
}

export function clearLocalDraftEdit(id: string): void {
  storage()?.removeItem(PREFIX + id);
}