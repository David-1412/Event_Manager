import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "@/lib/api";
import { establishSession } from "./session";
import { getAccessToken } from "./token-store";

/**
 * The session exchange decides whether the app believes the API trusts this
 * user, so both halves of its behaviour matter: falling back when the endpoint
 * doesn't exist, and *not* falling back when the endpoint exists and refuses the
 * credential. `fetch` is stubbed rather than mocked at the SDK level — the
 * module's dependency is HTTP, and `IdTokenCarrier` is a structural type, so a
 * literal object is a faithful stand-in for a Firebase `User`.
 */
function carrier(overrides: Partial<{ uid: string; email: string | null; displayName: string | null }> = {}) {
  return {
    uid: overrides.uid ?? "uid-123",
    getIdToken: vi.fn().mockResolvedValue("firebase-id-token"),
    // Explicit `undefined` checks: `??` would let an intended null be replaced
    // by the default, which is how "no display name" silently becomes "Sam Nguyen".
    displayName: overrides.displayName === undefined ? "Sam Nguyen" : overrides.displayName,
    email: overrides.email === undefined ? "sam@example.com" : overrides.email,
    photoURL: null,
    emailVerified: true,
  };
}

function response(status: number, body?: unknown): Response {
  return new Response(body === undefined ? "" : JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

const fetchMock = vi.fn();

beforeEach(() => {
  fetchMock.mockReset();
  vi.stubGlobal("fetch", fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("establishSession", () => {
  it("adopts the API's token and user when the endpoint exists", async () => {
    fetchMock.mockResolvedValue(
      response(200, {
        token: "api-access-token",
        user: { id: "db-user-1", displayName: "Sam N", email: "sam@example.com", avatarUrl: "https://cdn/a.png" },
      }),
    );

    const session = await establishSession(carrier());

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/auth/login",
      expect.objectContaining({ method: "POST" }),
    );
    expect(JSON.parse(String(fetchMock.mock.calls[0][1].body))).toEqual({
      idToken: "firebase-id-token",
      displayName: "Sam Nguyen",
      name: "Sam Nguyen",
    });
    expect(session.token).toBe("api-access-token");
    expect(session.apiTrusted).toBe(true);
    expect(session.user.id).toBe("db-user-1");
    expect(session.user.displayName).toBe("Sam N");
    expect(session.user.photoURL).toBe("https://cdn/a.png");
  });

  it("falls back to the Firebase ID token when the API has no auth endpoint", async () => {
    // 404 (no route), 405 (wrong verb) and 501 (not implemented) all mean the
    // same thing here: this backend does not verify tokens yet.
    for (const status of [404, 405, 501]) {
      fetchMock.mockResolvedValue(response(status, { status, title: "Not Found", code: "NotFound" }));
      const session = await establishSession(carrier());
      expect(session.apiTrusted, `status ${status}`).toBe(false);
      expect(session.token).toBe("firebase-id-token");
      expect(session.user.uid).toBe("uid-123");
      expect(session.user.id).toBeUndefined();
    }
  });

  it("still reads a moderator role from auth/me when login exchange is unavailable", async () => {
    fetchMock
      .mockResolvedValueOnce(response(404, { status: 404, title: "Not Found" }))
      .mockResolvedValueOnce(response(200, {
        id: "db-user-2",
        role: "Moderator",
        email: "sam@example.com",
        displayName: "Sam Nguyen",
      }));

    const session = await establishSession(carrier());

    expect(session.apiTrusted).toBe(false);
    expect(session.token).toBe("firebase-id-token");
    expect(session.user.role).toBe("Moderator");
  });

  it("falls back when the API is unreachable rather than logging the user out", async () => {
    fetchMock.mockRejectedValue(new TypeError("Failed to fetch"));
    const session = await establishSession(carrier());
    expect(session.apiTrusted).toBe(false);
    expect(session.token).toBe("firebase-id-token");
  });

  it("rejects when the API refuses the credential, so the form can show it", async () => {
    fetchMock.mockResolvedValue(
      response(401, { status: 401, title: "Unauthorized", detail: "Token expired", code: "Unknown" }),
    );
    // One call, one assertion chain: two `establishSession` calls would make the
    // second one reuse the mocked response body, which can only be read once.
    const rejection = await establishSession(carrier()).then(
      () => null,
      (error: unknown) => error,
    );
    expect(rejection).toBeInstanceOf(ApiError);
    expect(rejection).toMatchObject({ status: 401 });
  });

  it("propagates an abort to whoever owns the signal", async () => {
    fetchMock.mockRejectedValue(new DOMException("The operation was aborted.", "AbortError"));
    await expect(establishSession(carrier())).rejects.toBeInstanceOf(DOMException);
  });

  it("derives a display name from the email when Firebase has none", async () => {
    fetchMock.mockResolvedValue(response(404, { status: 404, title: "Not Found" }));
    const session = await establishSession(carrier({ displayName: null, email: "sam@x.com" }));
    expect(session.user.displayName).toBe("sam");
  });

  it("keeps the user signed in to Firebase when the response isn't the shape promised", async () => {
    // A 200 with no `token` (a proxy returning HTML, a gateway page) is not a
    // reason to lose a session.
    fetchMock.mockResolvedValue(new Response("<html>punted</html>", { status: 200 }));
    const session = await establishSession(carrier());
    expect(session.apiTrusted).toBe(false);
    expect(session.token).toBe("firebase-id-token");
  });
});

describe("token-store", () => {
  it("is empty until a session writes it, and clears back to empty", async () => {
    fetchMock.mockResolvedValue(response(200, { token: "t-1", user: { id: "u", displayName: "S", email: "e@x.com", avatarUrl: null } }));
    expect(getAccessToken()).toBeNull();
    await establishSession(carrier());
    // establishSession does not write the store — the provider does, so the
    // store's contract is a plain read/write/clear with no hidden state machine.
    const { setAccessToken, clearAccessToken } = await import("./token-store");
    setAccessToken("t-2");
    expect(getAccessToken()).toBe("t-2");
    clearAccessToken();
    expect(getAccessToken()).toBeNull();
  });
});
