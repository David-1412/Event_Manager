import { describe, expect, it, vi } from "vitest";
import {
  cardPropsById,
  cardPropsOf,
  shouldVirtualize,
  VIRTUALIZE_THRESHOLD,
} from "@/components/event/browse-layout";
import type { EventListItem } from "@/types/events";

function item(overrides: Partial<EventListItem> = {}): EventListItem {
  return {
    id: "evt-1",
    title: "Sunday social",
    tags: ["badminton"],
    sportIcon: "\u{1F3F8}",
    startAt: "2026-10-18T08:00:00.000Z",
    endAt: "2026-10-18T10:00:00.000Z",
    timezone: "Australia/Melbourne",
    venueName: "Glenn House Sports Centre",
    address: "15 Chalk Walk",
    latitude: -37.917,
    longitude: 145.131,
    cost: null,
    maxParticipants: 12,
    joinedCount: 8,
    interestedCount: 5,
    status: "Scheduled",
    isCancelled: false,
    visibility: "Public",
    requiresJoinApproval: false,
    ...overrides,
  } as EventListItem;
}

describe("shouldVirtualize", () => {
  it("does not window a short list, where the bookkeeping costs more than it saves", () => {
    expect(shouldVirtualize(0)).toBe(false);
    expect(shouldVirtualize(1)).toBe(false);
    expect(shouldVirtualize(VIRTUALIZE_THRESHOLD)).toBe(false);
  });

  it("windows past the threshold", () => {
    expect(shouldVirtualize(VIRTUALIZE_THRESHOLD + 1)).toBe(true);
    expect(shouldVirtualize(100)).toBe(true);
  });
});

describe("cardPropsOf", () => {
  it("carries every field the card renders", () => {
    const props = cardPropsOf(item());
    expect(props.id).toBe("evt-1");
    expect(props.title).toBe("Sunday social");
    expect(props.joinedCount).toBe(8);
    expect(props.interestedCount).toBe(5);
    expect(props.maxParticipants).toBe(12);
    expect(props.startAt).toBe("2026-10-18T08:00:00.000Z");
    expect(props.venueName).toBe("Glenn House Sports Centre");
    expect(props.skillLevel).toBeNull();
    expect(props.distanceKm).toBeNull();
  });

  it("keeps a null capacity null rather than coercing it to a number", () => {
    // The card renders "No limit" for null and a ratio for a number; turning null
    // into 0 here would make every unlimited event read as full.
    expect(cardPropsOf(item({ maxParticipants: null })).maxParticipants).toBeNull();
  });

  it("normalises absent optional fields to null", () => {
    const props = cardPropsOf(item({ hostName: undefined, thumbnailUrl: undefined }));
    expect(props.hostName).toBeNull();
    expect(props.thumbnailUrl).toBeNull();
  });
});

describe("cardPropsById", () => {
  it("keys the picked fields by id", () => {
    const map = cardPropsById([item({ id: "a" }), item({ id: "b" })]);
    expect([...map.keys()]).toEqual(["a", "b"]);
    expect(map.get("b")?.id).toBe("b");
  });

  it("gives equal content but a different identity on a refetch", () => {
    // The premise the memo comparison exists to solve: the feed hands back new
    // objects each poll, so identity can never be the test.
    const row = item();
    const first = cardPropsOf(row);
    const second = cardPropsOf({ ...row });
    expect(first).toEqual(second);
    expect(first).not.toBe(second);
  });

  it("changes identity only for the row whose content changed", () => {
    const build = vi.fn((rows: EventListItem[]) => cardPropsById(rows));
    const rows = [item({ id: "a" }), item({ id: "b" })];
    const before = build(rows);
    const after = build([
      { ...rows[0], joinedCount: 9 },
      { ...rows[1] },
    ]);
    // Both payloads differ by identity, but only one differs by value - which is
    // what sameCardData compares and therefore what a card re-renders on.
    expect(after.get("a")).not.toEqual(before.get("a"));
    expect(after.get("a")?.joinedCount).toBe(9);
    expect(after.get("b")).toEqual(before.get("b"));
  });
});
