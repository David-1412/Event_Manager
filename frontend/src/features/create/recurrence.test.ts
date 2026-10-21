import { afterEach, describe, expect, it } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import { createElement } from "react";
import { EventCard } from "@/components/event/event-card";
import {
  CREATE_EVENT_DEFAULTS,
  createEventSchema,
  toCreateEventPayload,
} from "@/features/create/create-event-schema";
import { expandDates, formatDay, repeatProblem, semesterEnd } from "@/features/create/recurrence";
import type { EventListItem } from "@/types/events";

/**
 * Recurring events, client side. The expansion here is a date-only copy of the
 * server's RecurrenceRule.Expand (backend RecurrenceRuleTests pins the same cases),
 * so the preview lists exactly the sessions the API will create.
 */
const days = (start: string, rule: Parameters<typeof expandDates>[1]) =>
  expandDates(start, rule).map(formatDay);

describe("expandDates", () => {
  it("follows the chosen weekdays and always includes the first session's day", () => {
    // Tue 3 Mar 2026; only Thursday ticked.
    expect(days("2026-03-03", { frequency: "WEEKLY", days: ["THU"], end: "count", count: 4 })).toEqual([
      "2026-03-03",
      "2026-03-05",
      "2026-03-10",
      "2026-03-12",
    ]);
  });

  it("skips alternate weeks for fortnightly", () => {
    expect(
      days("2026-03-04", { frequency: "BIWEEKLY", days: ["MON", "WED"], end: "count", count: 4 }),
    ).toEqual(["2026-03-04", "2026-03-16", "2026-03-18", "2026-03-30"]);
  });

  it("skips months without the day for monthly", () => {
    expect(days("2026-01-31", { frequency: "MONTHLY", days: [], end: "count", count: 3 })).toEqual([
      "2026-01-31",
      "2026-03-31",
      "2026-05-31",
    ]);
  });

  it("stops at the end date, inclusive", () => {
    expect(
      days("2026-03-03", { frequency: "WEEKLY", days: [], end: "date", until: "2026-03-17" }),
    ).toEqual(["2026-03-03", "2026-03-10", "2026-03-17"]);
  });
});

describe("semester preset", () => {
  it("ends 12 weeks after the first session less a day, giving 12 weekly sessions", () => {
    const until = semesterEnd("2026-03-02");
    expect(until).toBe("2026-05-24");
    const sessions = days("2026-03-02", { frequency: "WEEKLY", days: [], end: "date", until: until! });
    expect(sessions).toHaveLength(12);
    expect(sessions.at(-1)).toBe("2026-05-18");
  });
});

describe("repeatProblem", () => {
  it("accepts a normal series", () => {
    expect(repeatProblem("2026-03-02", { frequency: "WEEKLY", days: [], end: "count", count: 12 })).toBeNull();
  });

  it.each([
    [{ frequency: "WEEKLY", days: [], end: "date", until: "" }, /date the series ends/],
    [{ frequency: "WEEKLY", days: [], end: "date", until: "2026-03-01" }, /end after/],
    [{ frequency: "WEEKLY", days: [], end: "count", count: 1 }, /between 2 and 52/],
    [{ frequency: "WEEKLY", days: [], end: "count", count: 53 }, /between 2 and 52/],
    [{ frequency: "WEEKLY", days: [], end: "date", until: "2026-03-08" }, /only has one session/],
    [
      { frequency: "WEEKLY", days: ["MON", "TUE", "WED", "THU", "FRI"], end: "date", until: "2026-12-01" },
      /more than 52/,
    ],
  ] as const)("refuses %o", (rule, message) => {
    expect(repeatProblem("2026-03-02", { ...rule, days: [...rule.days] })).toMatch(message);
  });
});

function valid(overrides: Record<string, unknown> = {}) {
  return {
    ...CREATE_EVENT_DEFAULTS,
    title: "Tuesday Social Badminton",
    date: "2030-06-04",
    venueName: "Sports centre",
    address: "Wellington Rd, Clayton",
    latitude: -37.91,
    longitude: 145.13,
    ...overrides,
  };
}

describe("create payload", () => {
  it("sends no recurrence while Repeat is off", () => {
    const parsed = createEventSchema.parse(valid());
    expect(toCreateEventPayload(parsed).recurrence).toBeNull();
  });

  it("sends the rule with exactly one of endDate and count", () => {
    const byDate = createEventSchema.parse(
      valid({ repeat: true, repeatDays: ["THU"], repeatEnd: "date", repeatUntil: "2030-08-26" }),
    );
    expect(toCreateEventPayload(byDate).recurrence).toEqual({
      frequency: "WEEKLY",
      days: ["THU"],
      endDate: "2030-08-26",
      count: null,
    });

    const byCount = createEventSchema.parse(
      valid({ repeat: true, repeatFrequency: "MONTHLY", repeatDays: ["THU"], repeatEnd: "count", repeatCount: "6" }),
    );
    expect(toCreateEventPayload(byCount).recurrence).toEqual({
      frequency: "MONTHLY",
      days: [],
      endDate: null,
      count: 6,
    });
  });

  it("puts a bad schedule's error on the end field", () => {
    const result = createEventSchema.safeParse(valid({ repeat: true, repeatEnd: "count", repeatCount: "99" }));
    expect(result.success).toBe(false);
    expect(result.error?.issues[0]?.path).toEqual(["repeatCount"]);
  });
});

describe("repeat mark on the card", () => {
  afterEach(cleanup);

  const item = (overrides: Partial<EventListItem>): EventListItem => ({
    id: "evt-1",
    title: "Social hit",
    tags: [],
    sportIcon: null,
    skillLevel: null,
    startAt: "2030-06-04T08:00:00.000Z",
    endAt: "2030-06-04T10:00:00.000Z",
    timezone: "Australia/Melbourne",
    venueName: "Sports centre",
    address: "",
    thumbnailUrl: null,
    latitude: 0,
    longitude: 0,
    cost: null,
    maxParticipants: null,
    joinedCount: 0,
    interestedCount: 0,
    status: "Scheduled",
    isCancelled: false,
    visibility: "Public",
    distanceKm: null,
    ...overrides,
  });

  it("shows the repeat mark, labelled with the series, on a session", () => {
    render(
      createElement(EventCard, {
        event: item({ isRecurring: true, recurrenceSummary: "Weekly on Tue · 12 sessions" }),
      }),
    );
    expect(screen.getAllByRole("img", { name: "Repeats: Weekly on Tue · 12 sessions" }).length).toBeGreaterThan(0);
  });

  it("shows nothing on a one-off event", () => {
    render(createElement(EventCard, { event: item({}) }));
    expect(screen.queryByRole("img", { name: /Repeat/ })).toBeNull();
  });
});
