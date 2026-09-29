/**
 * Firebase surfaces failures as `FirebaseError` with a stable `code`
 * (`auth/invalid-credential`, …) and a message written for a console, not a
 * person — "There is no user record corresponding to this identifier" tells a
 * user nothing and tells an attacker plenty. This maps the codes we can actually
 * trigger from the two forms onto a sentence, and falls back to the code rather
 * than the message so an unmapped code still identifies itself to a developer.
 *
 * Auth-specific copy lives here rather than in `lib/format.ts` so the date/
 * distance formatters stay presentation-only helpers with no dependency on a
 * vendor's error vocabulary.
 */

/** The codes a correctly-typed password or Google popup can still produce. */
const SIGN_IN_MESSAGES: Record<string, string> = {
  "auth/invalid-credential": "We couldn't find an account with that email and password.",
  "auth/invalid-login-credentials": "We couldn't find an account with that email and password.",
  "auth/user-disabled": "This account has been disabled. Contact support.",
  "auth/too-many-requests": "Too many attempts. Wait a minute, then try again.",
  "auth/network-request-failed": "No connection. Check your network and try again.",
  "auth/not-allowed": "This browser blocks sign-in. Try a normal browser window.",
  "auth/operation-not-allowed": "This sign-in method isn't enabled for the project yet.",
  "auth/requires-recent-login": "For security, sign in again before doing that.",
};

/** Extra codes the register form can hit. */
const REGISTER_MESSAGES: Record<string, string> = {
  "auth/email-already-in-use": "That email already has an account. Try signing in instead.",
  "auth/weak-password": "Use at least 6 characters for your password.",
  "auth/invalid-email": "That doesn't look like an email address.",
  "auth/missing-password": "Enter a password.",
};

/** The codes that mean "the popup opened and was dismissed" — not an error. */
const DISMISSED_CODES = new Set([
  "auth/popup-closed-by-user",
  "auth/cancelled-popup-request",
  "auth/user-cancelled",
  "auth/popup-blocked",
]);

/**
 * The auth forms treat a closed popup as "nothing happened": no toast, no field
 * error, the button simply returns to idle. Rendering it as a failure would
 * teach people that Google sign-in is broken when they just pressed Cancel.
 */
export function isAuthCancelled(error: unknown): boolean {
  return DISMISSED_CODES.has(codeOf(error) ?? "");
}

/** True when the failure came from Firebase rather than our own validation. */
export function isAuthError(error: unknown): boolean {
  return codeOf(error) !== null;
}

/**
 * A person-facing sentence for any thrown value. Pass `form: "register"` to
 * include the signup-only codes; pass nothing for sign-in.
 */
export function describeAuthError(error: unknown, form: "signin" | "register" = "signin"): string {
  if (error instanceof Error && error.message && !isAuthError(error)) return error.message;
  const code = codeOf(error);
  if (!code) return "Something went wrong. Please try again.";
  if (DISMISSED_CODES.has(code)) return "Sign-in was cancelled.";
  const table = form === "register" ? { ...SIGN_IN_MESSAGES, ...REGISTER_MESSAGES } : SIGN_IN_MESSAGES;
  return table[code] ?? `Sign-in failed (${code.replace(/^auth\//, "firebase: ")}).`;
}

/** Reads `code` off a FirebaseError without importing the SDK into this module. */
function codeOf(error: unknown): string | null {
  if (typeof error !== "object" || error === null) return null;
  const code = (error as { code?: unknown }).code;
  return typeof code === "string" && code.startsWith("auth/") ? code : null;
}
