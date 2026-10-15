import { describe, expect, it } from "vitest";
import {
  canNativeShare,
  shareText,
  shareUrl,
  sharePayload,
} from "@/lib/share";

/**
 * The two rules that decide which share surface a person gets, plus the payload
 * that both surfaces send.
 *
 * `canNativeShare` is the branch the whole feature hangs on: answer yes where
 * there is no sheet and the tap does nothing, answer no where there is one and a
 * phone gets a copy-the-URL dialog instead of WhatsApp. The location shape is
 * passed in rather than read from a global so the URL rules are testable without
 * a browser.
 */
const LOC = {
  origin: "https://monahub.example",
  pathname: "/events/e-1",
  search: "",
};

describe("canNativeShare", () => {
  it("says yes when the browser exposes a share function", () => {
    expect(canNativeShare({ share: async () => {} } as Navigator)).toBe(true);
  });

  it("says no when the browser has no share function", () => {
    expect(canNativeShare({} as Navigator)).toBe(false);
  });

  it("says no when there is no navigator at all, which is the server pass", () => {
    expect(canNativeShare(undefined)).toBe(false);
  });

  it("says no when share exists but is not callable", () => {
    expect(canNativeShare({ share: "nope" } as unknown as Navigator)).toBe(false);
  });
});

describe("shareUrl", () => {
  it("returns the absolute event URL", () => {
    expect(shareUrl(LOC)).toBe("https://monahub.example/events/e-1");
  });

  it("drops browse search state so the recipient lands on the event, not a filtered list", () => {
    expect(shareUrl({ ...LOC, search: "?q=pickleball" })).toBe(
      "https://monahub.example/events/e-1",
    );
  });

  it("keeps unrelated query params, which may carry meaning the sender wants kept", () => {
    expect(shareUrl({ ...LOC, search: "?ref=share" })).toBe(
      "https://monahub.example/events/e-1?ref=share",
    );
  });

  it("tolerates a search written without its leading question mark", () => {
    expect(shareUrl({ ...LOC, search: "q=pickleball" })).toBe(
      "https://monahub.example/events/e-1",
    );
  });
});

describe("shareText", () => {
  it("collapses the whitespace of a multi-paragraph description into one line", () => {
    expect(shareText("Basketball at the park.\n\n  Bring water.")).toBe(
      "Basketball at the park. Bring water.",
    );
  });

  it("returns an empty string rather than the word null for a missing description", () => {
    expect(shareText(null)).toBe("");
    expect(shareText(undefined)).toBe("");
  });

  it("leaves a short description untouched", () => {
    expect(shareText("Casual run at the park")).toBe("Casual run at the park");
  });

  it("clamps a long description at a word boundary, so a chat preview is readable", () => {
    // A multi-syllable word, so a mid-word cut is actually detectable: slicing
    // "basketball" anywhere past the first letter leaves a fragment behind.
    const long = "basketball ".repeat(40).trim();
    const out = shareText(long, 40);
    expect(out.length).toBeLessThanOrEqual(41);
    expect(out.endsWith("\u2026")).toBe(true);
    // Three whole words fit inside 40 characters; the fourth must be dropped
    // whole rather than sliced, or the preview ends mid-word.
    expect(out).toBe("basketball basketball basketball\u2026");
  });
});

describe("sharePayload", () => {
  it("carries the title, the description and the URL together", () => {
    expect(
      sharePayload(
        { title: "Sunday social", description: "Casual basketball, all levels." },
        LOC,
      ),
    ).toEqual({
      title: "Sunday social",
      text: "Casual basketball, all levels.",
      url: "https://monahub.example/events/e-1",
    });
  });

  it("still produces a usable payload when the event has no description", () => {
    expect(sharePayload({ title: "Run club" }, LOC)).toEqual({
      title: "Run club",
      text: "",
      url: "https://monahub.example/events/e-1",
    });
  });
});