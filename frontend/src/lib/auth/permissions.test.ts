import { describe, expect, it } from "vitest";
import { parseUserRole, permissionsFor } from "./permissions";

/**
 * The client's copy of the backend permission table. These pin the rule the UI
 * leans on: a Creator publishes without review but sees nothing administrative,
 * and an unknown role (still loading, signed out) holds no permission at all.
 */
describe("permissionsFor", () => {
  it("gives a Member nothing beyond the basics", () => {
    expect(permissionsFor("Member")).toEqual({
      canPublishPublicEvents: false,
      canReviewEvents: false,
      canManageUsers: false,
    });
  });

  it("lets a Creator publish but not review or manage users", () => {
    expect(permissionsFor("Creator")).toEqual({
      canPublishPublicEvents: true,
      canReviewEvents: false,
      canManageUsers: false,
    });
  });

  it("gives an Admin everything", () => {
    expect(permissionsFor("Admin")).toEqual({
      canPublishPublicEvents: true,
      canReviewEvents: true,
      canManageUsers: true,
    });
  });

  it("treats an unknown role as no permission", () => {
    expect(permissionsFor(undefined)).toEqual(permissionsFor("Member"));
  });
});

describe("parseUserRole", () => {
  it("accepts the three role names", () => {
    expect(parseUserRole("Member")).toBe("Member");
    expect(parseUserRole("Creator")).toBe("Creator");
    expect(parseUserRole("Admin")).toBe("Admin");
  });

  it("rejects anything else rather than guessing", () => {
    expect(parseUserRole("admin")).toBeNull();
    expect(parseUserRole("Owner")).toBeNull();
    expect(parseUserRole(2)).toBeNull();
    expect(parseUserRole(undefined)).toBeNull();
  });
});
