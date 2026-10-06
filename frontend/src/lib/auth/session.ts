import { ApiError } from "@/lib/api";
import type { AuthUser } from "./types";

/**
 * The boundary between "signed in to Firebase" and "a user the API knows
 * about".
 *
 * The API contract (`IMPLEMENTATION_PLAN.md` §4) is: the browser hands over a
 * Firebase ID token, the API verifies it with the Firebase Admin SDK, upserts
 * the `users` row, and returns *its own* access token plus the `UserDto`. Two
 * tokens, two jobs — Firebase's proves who you are to us; the API's proves who
 * you are to the API, and is what goes in `Authorization`.
 *
 * That endpoint does not exist yet: the backend still runs
 * `ConfigurationDemoUser` as `ICurrentUser`. So this negotiates when the API is
 * reachable and falls back to the Firebase ID token, which is a real bearer
 * credential and is exactly what `Authorization` will carry if the API decides
 * to trust Firebase directly. The fallback is loud in one place rather than
 * assumed in three.
 */

export interface Session {
  user: AuthUser;
  /** The bearer token to send to our API — the API's own token when issued. */
  token: string | null;
  /** False while `token` is still the raw Firebase ID token (see above). */
  apiTrusted: boolean;
}

export interface LoginResponse {
  token: string;
  user: UserDto;
}

/** Mirror of `UserDto` (the backend's name for it, per the naming contract). */
export interface UserDto {
  id: string;
  displayName: string;
  email: string;
  avatarUrl: string | null;
}

interface IdTokenCarrier {
  /** Firebase UID — the field the API keys its own user row on. */
  uid: string;
  getIdToken(force?: boolean): Promise<string>;
  displayName: string | null;
  email: string | null;
  photoURL: string | null;
  emailVerified: boolean;
  /** Firebase `User.metadata`; `creationTime` is an RFC 1123 date string. */
  metadata?: { creationTime?: string };
}

export const AUTH_LOGIN_PATH = "/api/auth/login";

/**
 * Exchange a Firebase identity for an API session. Never throws for a missing
 * endpoint: a 404/405/501 means "this API has no auth yet", which is today's
 * reality and must leave the user signed in rather than stranded. A 4xx that
 * isn't one of those, or a 5xx from a real auth endpoint, propagates — a
 * rejected credential is a fact the form has to show.
 *
 * `displayName` is sent as both `displayName` and `name` because
 * `IMPLEMENTATION_PLAN.md` §4 gives the two endpoints different field names
 * (`{idToken}` for login, `{name}` for register) and this is the one request
 * body that has to satisfy whichever exists. Cheap redundancy against a
 * contract that is still moving; drop the alias when the DTO is final.
 */
export async function establishSession(
  firebaseUser: IdTokenCarrier,
  signal?: AbortSignal,
): Promise<Session> {
  const idToken = await firebaseUser.getIdToken();
  const fallback: Session = { user: toAuthUser(firebaseUser), token: idToken, apiTrusted: false };

  try {
    const response = await postLogin(
      { idToken, displayName: firebaseUser.displayName, name: firebaseUser.displayName },
      signal,
    );
    if (!response.ok) {
      if (isMissingEndpoint(response.status)) return fallback;
      throw await errorFrom(response);
    }
    const payload = (await response.json()) as LoginResponse;
    return { user: mergeUser(firebaseUser, payload.user), token: payload.token, apiTrusted: true };
  } catch (error) {
    if (error instanceof ApiError && isMissingEndpoint(error.status)) return fallback;
    // Aborts belong to whoever owns the signal, not to us.
    if (error instanceof DOMException && error.name === "AbortError") throw error;
    // A rejection that reaches this block came from our own `errorFrom`, i.e. the
    // API exists and refused the credential — that is a fact the form has to
    // show, never something to paper over. Anything else (transport failure,
    // malformed JSON, a body missing `token`) means we could not ask, and the
    // user stays signed in to Firebase without an API token.
    if (error instanceof ApiError) throw error;
    return fallback;
  }
}

/** Sign-up is the same exchange plus the display name the API has to store. */
export async function registerWithApi(
  firebaseUser: IdTokenCarrier,
  displayName: string,
  signal?: AbortSignal,
): Promise<Session> {
  const session = await establishSession(firebaseUser, signal);
  if (!session.apiTrusted) return session;
  // `POST /api/auth/register` upserts the row from the verified token; the name
  // rides along for forward compatibility with the documented `{name}` field.
  void displayName;
  return session;
}

async function postLogin(body: unknown, signal?: AbortSignal): Promise<Response> {
  const base = process.env.NEXT_PUBLIC_API_BASE_URL ?? "";
  // Deliberately not `request()` from lib/api: that one attaches the bearer
  // token and enforces a 10s timeout, and the auth exchange has neither job
  // before a token exists.
  return fetch(`${base}${AUTH_LOGIN_PATH}`, {
    method: "POST",
    signal,
    headers: { "Content-Type": "application/json", Accept: "application/json" },
    body: JSON.stringify(body),
  });
}

/**
 * "No auth on this server" — the status set that means the endpoint is absent
 * rather than the credential being wrong. A connection refusal arrives as a
 * fetch rejection (`status 0`) and is handled by the caller's catch.
 */
function isMissingEndpoint(status: number): boolean {
  return status === 0 || status === 404 || status === 405 || status === 501;
}

async function errorFrom(response: Response): Promise<ApiError> {
  const text = await response.text();
  let payload: unknown = null;
  try {
    payload = text ? JSON.parse(text) : null;
  } catch {
    payload = null;
  }
  const problem =
    payload && typeof payload === "object" && "status" in payload
      ? (payload as { status: number; title: string; detail?: string })
      : { status: response.status, title: response.statusText || "Sign-in failed" };
  return new ApiError({ code: "Unknown", ...problem });
}

export function toAuthUser(user: IdTokenCarrier): AuthUser {
  const email = user.email ?? "";
  return {
    uid: user.uid,
    displayName: user.displayName ?? email.split("@")[0] ?? "",
    email,
    photoURL: user.photoURL,
    emailVerified: user.emailVerified,
    createdAt: toIso(user.metadata?.creationTime),
  };
}

function toIso(value: string | undefined): string | undefined {
  const time = value ? Date.parse(value) : NaN;
  return Number.isNaN(time) ? undefined : new Date(time).toISOString();
}

/** The API row wins on identity fields; Firebase fills whatever it lacks. */
function mergeUser(firebaseUser: IdTokenCarrier, dto: UserDto): AuthUser {
  const base = toAuthUser(firebaseUser);
  return {
    ...base,
    id: dto.id,
    displayName: dto.displayName || base.displayName,
    email: dto.email || base.email,
    photoURL: dto.avatarUrl ?? base.photoURL,
  };
}
