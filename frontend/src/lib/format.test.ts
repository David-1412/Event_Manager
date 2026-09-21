import { describe, expect, it } from "vitest";
import {
  formatCardWhen,
  formatCost,
  formatDayMonth,
  formatDistance,
  formatFullDate,
  formatRelativeDayTime,
  formatTime,
  formatTimeRange,
  isValidDate,
  spotsLeft,
  spotsTakenSentence,
} from "@/lib/format";

/**
 * `lib/format.ts` is the only module allowed to do date maths (spec §12), so it
 * carries the edge cases: Melbourne DST boundaries, runs crossing midnight, and
 * input that must never surface as `Invalid Date` (spec §13).
 *
 * Instants are written as UTC ISO strings so the expectations do not depend on
 * the machine running the tests. AEST is UTC+10 (summer, DST on) and AEST
 * standard is UTC+10 / AEDT UTC+11 - Melbourne springs forward on the first
 * Sunday of October and falls back on the first Sunday of April.
 */

/** 2026-02-16 is a Monday in AEDT (UTC+11). */
const MON_6PM_AEDT = "2026-02-16T07:00:00Z"; // 18:00 AEDT
const MON_8PM_AEDT = "2026-02-16T09:00:00Z"; // 20:00 AEDT

describe("isValidDate", () => {
  it("rejects junk rather than propagating it", () => {
    expect(isValidDate("not a date")).toBe(false);
    expect(isValidDate("")).toBe(false);
    expect(isValidDate(MON_6PM_AEDT)).toBe(true);
  });
});

describe("formatTime", () => {
  it("renders Melbourne wall time regardless of the server timezone", () => {
    expect(formatTime(MON_6PM_AEDT)).toBe("6PM");
  });

  it("keeps minutes when they are not :00", () => {
    expect(formatTime("2026-02-16T07:30:00Z")).toBe("6:30PM");
  });

  it("renders midnight as 12AM, not 0AM or Invalid Date", () => {
    expect(formatTime("2026-02-16T13:00:00Z")).toBe("12AM"); // 11pm+1h = midnight AEDT
  });

  it("returns an em dash for invalid input", () => {
    expect(formatTime("garbage")).toBe("\u2014");
  });
});

describe("DST boundaries", () => {
  it("formats a date on the spring-forward Sunday in Melbourne local time", () => {
    // Melbourne springs forward 2026-10-04 at 02:00 -> 03:00 local, so this day
    // is already AEDT (UTC+11): 01:00Z is 12:00pm local, not 11am.
    expect(formatTime("2026-10-04T01:00:00Z")).toBe("12PM");
    expect(formatFullDate("2026-10-04T01:00:00Z")).toBe("Sunday 4 October");
    expect(formatDayMonth("2026-10-04T01:00:00Z")).toBe("Sun 4 Oct");
  });

  it("keeps the same wall clock across the autumn fallback Sunday", () => {
    // Falls back 2026-04-05 at 03:00 -> 02:00 local, so the day is AEST UTC+10.
    // 13:00Z is therefore 23:00 local, not 22:00.
    expect(formatTime("2026-04-05T13:00:00Z")).toBe("11PM");
  });

  it("does not shift the Melbourne day when UTC and Melbourne disagree", () => {
    // 2026-02-16T13:30:00Z is already 00:30 on the 17th in Melbourne.
    expect(formatFullDate("2026-02-16T13:30:00Z")).toContain("Tuesday");
  });
});

describe("formatTimeRange", () => {
  it("collapses a shared meridiem", () => {
    expect(formatTimeRange(MON_6PM_AEDT, MON_8PM_AEDT)).toBe("6\u20138PM");
  });

  it("keeps minutes only where they differ", () => {
    expect(
      formatTimeRange("2026-02-16T07:15:00Z", "2026-02-16T09:30:00Z"),
    ).toBe("6:15\u20138:30PM");
  });

  it("shows both meridiems when the run crosses noon", () => {
    // 22:00Z is 9:00am AEDT and 02:30Z the next hour-block is 1:30pm AEDT, so
    // the collapsed form cannot be used.
    expect(formatTimeRange("2026-02-16T22:00:00Z", "2026-02-17T02:30:00Z")).toBe(
      "9AM\u20131:30PM",
    );
  });

  it("shows both meridiems when the run crosses midnight", () => {
    expect(formatTimeRange(MON_6PM_AEDT, "2026-02-16T14:00:00Z")).toBe("6PM\u20131AM");
  });

  it("returns an em dash when either end is invalid", () => {
    expect(formatTimeRange("nope", MON_8PM_AEDT)).toBe("\u2014");
    expect(formatTimeRange(MON_6PM_AEDT, "")).toBe("\u2014");
  });
});

describe("formatRelativeDayTime", () => {
  const now = new Date("2026-02-16T01:00:00Z"); // Mon 11am AEDT

  it("uses Today before 5pm and Tonight from 5pm on the same Melbourne day", () => {
    expect(formatRelativeDayTime("2026-02-16T00:00:00Z", now)).toBe("Today 11AM");
    expect(formatRelativeDayTime(MON_6PM_AEDT, now)).toBe("Tonight 6PM");
  });

  it("flips out of Tonight at midnight even though only an hour passed", () => {
    const lateNight = new Date("2026-02-16T12:30:00Z"); // Mon 11:30pm AEDT
    // An hour later it is Tuesday locally, so "Tonight" no longer applies.
    expect(formatRelativeDayTime("2026-02-16T13:30:00Z", lateNight)).not.toMatch(/Tonight/);
  });

  it("uses Tomorrow for the next Melbourne day", () => {
    expect(formatRelativeDayTime("2026-02-17T07:00:00Z", now)).toBe("Tomorrow 6PM");
  });

  it("falls back to a weekday and date beyond tomorrow", () => {
    expect(formatRelativeDayTime("2026-02-21T07:00:00Z", now)).toBe("Sat 21 Feb 6PM");
  });
});

describe("formatCardWhen", () => {
  const now = new Date("2026-02-16T01:00:00Z");

  it("composes the relative label with the collapsed time range", () => {
    expect(
      formatCardWhen({ startAt: MON_6PM_AEDT, endAt: MON_8PM_AEDT }, now),
    ).toBe("Tonight 6\u20138PM");
  });

  it("never leaks Invalid Date", () => {
    expect(
      formatCardWhen({ startAt: "nonsense", endAt: MON_8PM_AEDT }, now),
    ).toBe("\u2014");
  });
});

describe("formatCost", () => {
  it("renders Free for null and for zero (spec §12)", () => {
    expect(formatCost(null)).toBe("Free");
    expect(formatCost(0)).toBe("Free");
  });

  it("drops trailing zeros on whole dollars and keeps cents otherwise", () => {
    expect(formatCost(15)).toBe("$15");
    expect(formatCost(12.5)).toBe("$12.50");
  });
});

describe("formatDistance", () => {
  it("switches unit by magnitude rather than always using km", () => {
    expect(formatDistance(null)).toBe("Nearby");
    expect(formatDistance(0.05)).toBe("On-site");
    expect(formatDistance(0.32)).toBe("320 m away");
    expect(formatDistance(3.14)).toBe("3.1 km away");
    expect(formatDistance(23.6)).toBe("24 km away");
  });
});

describe("count copy", () => {
  it("always spells the live-region sentence out", () => {
    expect(spotsTakenSentence(2, 4)).toBe("2 of 4 spots taken");
    expect(spotsTakenSentence(0, 1)).toBe("0 of 1 spots taken");
  });

  it("never returns a negative number of spots", () => {
    expect(spotsLeft(2, 4)).toBe(2);
    expect(spotsLeft(5, 4)).toBe(0);
  });
});
