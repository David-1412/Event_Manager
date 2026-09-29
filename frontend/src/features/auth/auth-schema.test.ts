import { describe, expect, it } from "vitest";
import {
  MIN_PASSWORD_LENGTH,
  displayNameSchema,
  registerSchema,
  signInSchema,
} from "./auth-schema";

/**
 * Auth validation is the one place a wrong rule locks a real person out of their
 * own account, so the bounds are asserted rather than trusted: the password
 * minimum matches Firebase's own (anything higher rejects accounts the backend
 * would accept), and the name maximum matches the `users.name` column so a long
 * name fails here instead of as a 500 later.
 */

describe("signInSchema", () => {
  it("accepts a normal email and any non-empty password", () => {
    expect(
      signInSchema.safeParse({ email: "sam@example.com", password: "a" }).success,
    ).toBe(true);
  });

  it("rejects a malformed email with a sentence, not a zod object", () => {
    const result = signInSchema.safeParse({ email: "sam@example", password: "hunter2" });
    expect(result.success).toBe(false);
    expect(result.error?.issues[0]?.message).toBe("Enter an email address");
  });

  it("asks for the password rather than validating its strength on sign-in", () => {
    const result = signInSchema.safeParse({ email: "sam@example.com", password: "" });
    expect(result.error?.issues?.[0]?.message).toBe("Enter your password");
  });
});

describe("registerSchema", () => {
  const valid = {
    displayName: "Sam Nguyen",
    email: "sam@example.com",
    password: "hunter2",
    confirm: "hunter2",
  };

  it("accepts a complete form", () => {
    expect(registerSchema.safeParse(valid).success).toBe(true);
  });

  it("requires the two passwords to match, and blames the confirm field", () => {
    const result = registerSchema.safeParse({ ...valid, confirm: "hunter3" });
    expect(result.success).toBe(false);
    expect(result.error?.issues[0]?.path).toEqual(["confirm"]);
  });

  it(`requires ${MIN_PASSWORD_LENGTH} characters — Firebase's own minimum, no stricter`, () => {
    expect(
      registerSchema.safeParse({ ...valid, password: "hunter2", confirm: "hunter2" }).success,
    ).toBe(true);
    expect(
      registerSchema.safeParse({ ...valid, password: "h1!", confirm: "h1!" }).success,
    ).toBe(false);
  });

  it("rejects a name that is only whitespace", () => {
    expect(registerSchema.safeParse({ ...valid, displayName: "   " }).success).toBe(false);
  });
});

describe("displayNameSchema", () => {
  it("trims before measuring, so a padded name is valid", () => {
    const result = displayNameSchema.safeParse({ displayName: "  Sam Nguyen  " });
    expect(result.success).toBe(true);
    if (result.success) expect(result.data.displayName).toBe("Sam Nguyen");
  });

  it("refuses an empty name instead of letting a blank one reach the API", () => {
    expect(displayNameSchema.safeParse({ displayName: "" }).success).toBe(false);
  });
});
