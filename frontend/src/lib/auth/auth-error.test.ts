import { describe, expect, it } from "vitest";
import { describeAuthError, isAuthCancelled, isAuthError } from "./auth-error";

/**
 * `FirebaseError` is a plain object with a `code` as far as this module is
 * concerned — it never imports the SDK, which is what keeps this file testable
 * without a network or a Firebase project. Constructing the same shape here is
 * the contract test: if the SDK ever changes the field, this stops matching and
 * the mapping has to be revisited, loudly.
 */
function firebaseError(code: string): Error & { code: string } {
  const error = new Error(`Firebase said: ${code}`) as Error & { code: string };
  error.name = "FirebaseError";
  error.code = code;
  return error;
}

describe("describeAuthError", () => {
  it("turns a bad credential into one sentence that doesn't say which half was wrong", () => {
    const message = describeAuthError(firebaseError("auth/invalid-credential"));
    expect(message).toBe("We couldn't find an account with that email and password.");
    // Firebase retired `auth/wrong-password` precisely so a client cannot tell
    // "no such user" from "bad password"; the copy has to keep that property.
    expect(message.toLowerCase()).not.toContain("email address");
  });

  it("maps the register-only codes when told it's the register form", () => {
    expect(describeAuthError(firebaseError("auth/email-already-in-use"), "register")).toBe(
      "That email already has an account. Try signing in instead.",
    );
    expect(describeAuthError(firebaseError("auth/weak-password"), "register")).toContain("6 characters");
  });

  it("still explains a register code on the sign-in form via the shared table", () => {
    expect(describeAuthError(firebaseError("auth/too-many-requests"))).toContain("Too many attempts");
  });

  it("names an unmapped code instead of showing Firebase's developer message", () => {
    const message = describeAuthError(firebaseError("auth/configuration-not-found"));
    expect(message).toContain("firebase: configuration-not-found");
    expect(message).not.toContain("Firebase said");
  });

  it("passes a non-Firebase error's own message through", () => {
    expect(describeAuthError(new Error("Sign-in isn't configured for this deployment."))).toBe(
      "Sign-in isn't configured for this deployment.",
    );
  });

  it("falls back to a generic sentence for anything unrecognisable", () => {
    expect(describeAuthError(undefined)).toBe("Something went wrong. Please try again.");
    expect(describeAuthError("auth/invalid-credential")).toBe("Something went wrong. Please try again.");
  });

  it("reports a dismissed popup as a cancellation rather than a failure", () => {
    expect(describeAuthError(firebaseError("auth/popup-closed-by-user"))).toBe("Sign-in was cancelled.");
  });
});

describe("isAuthCancelled", () => {
  it.each([
    "auth/popup-closed-by-user",
    "auth/cancelled-popup-request",
    "auth/user-cancelled",
    "auth/popup-blocked",
  ])("treats %s as 'nothing happened'", (code) => {
    expect(isAuthCancelled(firebaseError(code))).toBe(true);
  });

  it("does not treat a real rejection as a cancellation", () => {
    expect(isAuthCancelled(firebaseError("auth/invalid-credential"))).toBe(false);
    expect(isAuthCancelled(null)).toBe(false);
  });
});

describe("isAuthError", () => {
  it("recognises only the SDK's own codes", () => {
    expect(isAuthError(firebaseError("auth/network-request-failed"))).toBe(true);
    expect(isAuthError(new Error("boom"))).toBe(false);
    expect(isAuthError({ message: "no code" })).toBe(false);
  });
});
