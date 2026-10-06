/**
 * The signed-in person, as the app sees them.
 *
 * Deliberately *not* `firebase/auth`'s `User`: that type would then appear in
 * every component's props and the SDK would leak through the whole tree —
 * `IMPLEMENTATION_PLAN.md` §5 keeps Firebase behind an interface precisely so
 * the swap to another provider stays a one-file change. Everything here is
 * plain data the UI already renders.
 */
export interface AuthUser {
  /** Firebase UID. Always present while signed in. */
  uid: string;
  /** Our API's `users.id`, present once the API has issued a `UserDto`. */
  id?: string;
  displayName: string;
  email: string;
  photoURL: string | null;
  emailVerified: boolean;
  /** ISO timestamp the account was created, when the provider reports it. */
  createdAt?: string;
}

/**
 * Where a write that needs an identity should send the user instead — a path,
 * URL-encoded into `/login?next=` so it survives the redirect intact. One
 * function because every guarded action (join, create, the account page's
 * sign-in button) has to build the same string, and a hand-written one tends to
 * forget the encoding, which breaks any path with a query string.
 */
export function signInReturnTo(path: string): string {
  return `/login?next=${encodeURIComponent(path)}`;
}
