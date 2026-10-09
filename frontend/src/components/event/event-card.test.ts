import { describe, expect, it } from "vitest";
import { eventCardVariant } from "@/components/event/event-card";
import { isPendingReviewStatus, isPublishedStatus } from "@/types/events";
import type { EventListItem } from "@/types/events";

/**
 * The review workflow's client-side half. `eventCardVariant` is what decides
 * whether a card advertises open slots, so the ordering here is the difference
 * between "come join this" and "this is waiting on a decision".
 */
function item(overrides: Partial<EventListItem> = {}): EventListItem {
  return {
    id: "evt-1",
    title: "Sunday social",
    tags: ["social"],
    sportIcon: null,
    skillLevel: null,
    startAt: "2026-11-01T09:00:00.000Z",
    endAt: "2026-11-01T11:00:00.000Z",
    timezone: "Australia/Melbourne",
    venueName: "Test courts",
    address: "1 Test St",
    thumbnailUrl: null,
    latitude: 0,
    longitude: 0,
    cost: null,
    maxParticipants: 10,
    joinedCount: 2,
    interestedCount: 1,
    status: "Scheduled",
    isCancelled: false,
    visibility: "Public",
    distanceKm: null,
    ...overrides,
  };
}

describe("isPublishedStatus", () => {
  it("treats Scheduled and Published as the two live statuses", () => {
    expect(isPublishedStatus("Scheduled")).toBe(true);
    expect(isPublishedStatus("Published")).toBe(true);
    expect(isPublishedStatus("PendingReview")).toBe(false);
    expect(isPublishedStatus("Draft")).toBe(false);
    expect(isPublishedStatus("Rejected")).toBe(false);
    expect(isPublishedStatus("Cancelled")).toBe(false);
    expect(isPublishedStatus("Completed")).toBe(false);
  });
});

describe("eventCardVariant with a pending-review event", () => {
  it("reports pending-review rather than mine for the host", () => {
    // The case that matters: a host must not see "You're hosting" plus open
    // availability on an event nobody can find or join yet.
    expect(eventCardVariant(item({ status: "PendingReview" }), { isHost: true })).toBe(
      "pending-review",
    );
  });

  it("reports pending-review rather than an availability variant", () => {
    expect(eventCardVariant(item({ status: "PendingReview", joinedCount: 10 }))).toBe(
      "pending-review",
    );
    expect(
      eventCardVariant(item({ status: "PendingReview", joinedCount: 9 }), { isJoined: true }),
    ).toBe("pending-review");
  });

  it("outranks Completed but loses to Cancelled", () => {
    // Cancelled wins because a withdrawn event is a stronger fact than a
    // submission still in flight; pending beats Completed because an event
    // awaiting review has not happened.
    expect(
      eventCardVariant(item({ status: "PendingReview", isCancelled: true }), { isHost: true }),
    ).toBe("cancelled");
    expect(eventCardVariant(item({ status: "Completed" }), { isHost: true })).toBe("ended");
  });

  it("leaves the live statuses on their existing variants", () => {
    expect(eventCardVariant(item(), { isHost: true })).toBe("mine");
    expect(eventCardVariant(item({ status: "Published" }), { isHost: true })).toBe("mine");
    expect(eventCardVariant(item({ status: "Rejected" }), { isHost: true })).toBe("mine");
  });
});
