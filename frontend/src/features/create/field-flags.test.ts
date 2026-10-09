import { describe, expect, it } from "vitest";
import {
  activeFlags,
  confidenceBand,
  flagText,
  raiseFlags,
} from "./field-flags";
import { CREATE_EVENT_DEFAULTS, type CreateEventValues } from "./create-event-schema";
import type { ImportFlag } from "@/types/events";

const base: CreateEventValues = {
  ...CREATE_EVENT_DEFAULTS,
  title: "Social badminton",
  date: "2026-11-14",
  startTime: "19:30",
  endTime: "21:30",
  maxParticipants: 12,
};
const venue = { venueName: "Seddon Park", address: "42 Railway Ave", latitude: -37.81, longitude: 144.89 };
const none = new Set<never>();

const flags: ImportFlag[] = [
  { field: "startAt", reason: "unclear" },
  { field: "venue", reason: "unconfirmed" },
  { field: "endAt", reason: "assumed" },
];

describe("raised flags", () => {
  it("are all active straight after the form is filled", () => {
    const raised = raiseFlags(flags, base, venue);
    expect(activeFlags(raised, none, base, venue)).toEqual({
      startAt: "unclear",
      venue: "unconfirmed",
      endAt: "assumed",
    });
  });

  it("clear when the field they point at is edited, and only that one", () => {
    const raised = raiseFlags(flags, base, venue);
    const edited = { ...base, startTime: "20:00" };
    expect(activeFlags(raised, none, edited, venue)).toEqual({
      venue: "unconfirmed",
      endAt: "assumed",
    });
  });

  it("clear when the date is edited as well as the time", () => {
    const raised = raiseFlags(flags, base, venue);
    expect(activeFlags(raised, none, { ...base, date: "2026-11-21" }, venue).startAt).toBeUndefined();
  });

  it("clear when the pin moves", () => {
    const raised = raiseFlags(flags, base, venue);
    const moved = { ...venue, latitude: -37.9 };
    expect(activeFlags(raised, none, base, moved).venue).toBeUndefined();
  });

  it("clear when a missing venue is picked", () => {
    const raised = raiseFlags([{ field: "venue", reason: "missing" }], base, null);
    expect(activeFlags(raised, none, base, null).venue).toBe("missing");
    expect(activeFlags(raised, none, base, venue).venue).toBeUndefined();
  });

  it("clear when dismissed with Looks right", () => {
    const raised = raiseFlags(flags, base, venue);
    const active = activeFlags(raised, new Set(["venue" as const]), base, venue);
    expect(active.venue).toBeUndefined();
    expect(active.startAt).toBe("unclear");
  });

  it("a new import replaces the old flags rather than adding to them", () => {
    const first = raiseFlags(flags, base, venue);
    const second = raiseFlags([{ field: "title", reason: "unclear" }], base, venue);
    expect(Object.keys(first)).toHaveLength(3);
    expect(Object.keys(second)).toEqual(["title"]);
  });
});

describe("confidence band", () => {
  it.each([
    [0.9, "ready"],
    [0.8, "ready"],
    [0.65, "check"],
    [0.5, "check"],
    [0.3, "low"],
    [null, "low"],
  ] as const)("%s reads as %s", (confidence, band) => {
    expect(confidenceBand({ confidence, basic: false })).toBe(band);
  });

  it("a basic reading is its own band whatever its number says", () => {
    expect(confidenceBand({ confidence: 0.9, basic: true })).toBe("basic");
  });
});

describe("flag text", () => {
  it("has a sentence for every reason", () => {
    for (const reason of ["missing", "assumed", "unclear", "past", "unconfirmed"] as const) {
      expect(flagText(reason).length).toBeGreaterThan(5);
    }
  });
});
