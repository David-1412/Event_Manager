/**
 * The access token the API should see for the current session.
 *
 * A module variable rather than React state, because the callers that need it
 * are not components: `lib/api.ts`'s `request()` and the SWR fetcher both run
 * outside the component tree, and passing a token down through props would mean
 * threading auth through every data hook in the app. The provider owns the
 * value (it writes on session change, clears on sign-out) and components read
 * it through `useAuth()`, so there is still exactly one writer.
 *
 * Firebase already persists the session across reloads, so nothing here needs
 * its own storage; on boot `getCurrentToken()` resolves from the restored
 * Firebase user before the first authenticated request goes out.
 */
let accessToken: string | null = null;

export function setAccessToken(token: string | null): void {
  accessToken = token;
}

export function getAccessToken(): string | null {
  return accessToken;
}

/** Guards against a stale session's token surviving a failed sign-out. */
export function clearAccessToken(): void {
  accessToken = null;
}
