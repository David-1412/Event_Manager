import { describe, expect, it } from "vitest";
import { resolveApiBaseUrl } from "@/lib/api";

/**
 * On an https page a plain-http API address is blocked by the browser (mixed
 * content), so calls go to the site's own origin and the web server forwards them.
 */
describe("resolveApiBaseUrl", () => {
  it("uses the site's own origin when the page is https and the API is http", () => {
    expect(resolveApiBaseUrl("http://31.220.109.12:5000", "https:")).toBe("");
  });

  it("keeps the configured address otherwise", () => {
    expect(resolveApiBaseUrl("https://api.example.com", "https:")).toBe("https://api.example.com");
    expect(resolveApiBaseUrl("http://localhost:5000", "http:")).toBe("http://localhost:5000");
    // Server-side rendering has no page protocol.
    expect(resolveApiBaseUrl("http://localhost:5000", undefined)).toBe("http://localhost:5000");
  });
});
