/**
 * Recurring events, client side: the day codes, the semester preset, and a date-only
 * copy of the server's RecurrenceRule.Expand so the form can preview the sessions
 * and refuse a schedule the API would reject before it is sent.
 *
 * Works on calendar dates ("2026-03-02") only. The server owns the times - it keeps
 * the first session's local start time on every date, through daylight saving - so
 * the preview has no time arithmetic to get wrong.
 */

export type RepeatFrequency = "WEEKLY" | "BIWEEKLY" | "MONTHLY";
export type DayCode = "MON" | "TUE" | "WED" | "THU" | "FRI" | "SAT" | "SUN";

export const DAY_CODES: DayCode[] = ["MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN"];
export const DAY_LETTERS: Record<DayCode, string> = {
  MON: "M", TUE: "T", WED: "W", THU: "T", FRI: "F", SAT: "S", SUN: "S",
};
export const DAY_NAMES: Record<DayCode, string> = {
  MON: "Monday", TUE: "Tuesday", WED: "Wednesday", THU: "Thursday",
  FRI: "Friday", SAT: "Saturday", SUN: "Sunday",
};

export const FREQUENCY_LABELS: Record<RepeatFrequency, string> = {
  WEEKLY: "Weekly",
  BIWEEKLY: "Fortnightly",
  MONTHLY: "Monthly",
};

/** Matches RecurrenceRule.MaxOccurrences on the server. */
export const MAX_SESSIONS = 52;
/** Matches RecurrenceRule.MaxSpanDays on the server. */
const MAX_SPAN_DAYS = 366;

/** A semester of teaching: 12 weekly sessions, so it ends 12 weeks after the first
 *  session, less a day. */
export const SEMESTER_WEEKS = 12;

export interface RepeatRule {
  frequency: RepeatFrequency;
  days: string[];
  end: "date" | "count";
  until?: string;
  count?: number;
}

/** `YYYY-MM-DD` to a UTC-midnight Date, or null. UTC so adding days never meets a
 *  daylight-saving gap. */
export function parseDay(value: string | undefined): Date | null {
  if (!value || !/^\d{4}-\d{2}-\d{2}$/.test(value)) return null;
  const d = new Date(`${value}T00:00:00Z`);
  return Number.isNaN(d.getTime()) ? null : d;
}

export function formatDay(d: Date): string {
  return d.toISOString().slice(0, 10);
}

export function addDays(d: Date, days: number): Date {
  return new Date(d.getTime() + days * 86_400_000);
}

/** The day code of a date (Monday-first, as the server stores them). */
export function dayCodeOf(d: Date): DayCode {
  return DAY_CODES[(d.getUTCDay() + 6) % 7];
}

/** The end date the semester preset fills in for a series starting on `start`. */
export function semesterEnd(start: string): string | null {
  const first = parseDay(start);
  return first ? formatDay(addDays(first, SEMESTER_WEEKS * 7 - 1)) : null;
}

/**
 * The session dates of a series starting on `start`, as the server will expand them.
 * Stops one past MAX_SESSIONS so the caller can tell "too many" from "exactly 52".
 */
export function expandDates(start: string, rule: RepeatRule): Date[] {
  const first = parseDay(start);
  if (!first) return [];
  const until = rule.end === "date" ? parseDay(rule.until) : null;
  const count = rule.end === "count" ? rule.count : undefined;
  const last = addDays(first, MAX_SPAN_DAYS);
  const limit = Math.min(count ?? Infinity, MAX_SESSIONS + 1);

  const out: Date[] = [];
  const take = (d: Date) => {
    if (until && d > until) return false;
    if (out.length >= limit) return false;
    out.push(d);
    return true;
  };

  if (rule.frequency === "MONTHLY") {
    const day = first.getUTCDate();
    for (let i = 0; i <= 12; i++) {
      const d = new Date(Date.UTC(first.getUTCFullYear(), first.getUTCMonth() + i, day));
      if (d.getUTCDate() !== day) continue; // the 31st in a 30-day month: skipped
      if (!take(d)) break;
    }
    return out;
  }

  const days = new Set([...rule.days, dayCodeOf(first)]);
  const weekStart = addDays(first, -((first.getUTCDay() + 6) % 7));
  for (let d = first; d <= last; d = addDays(d, 1)) {
    const week = Math.floor((d.getTime() - weekStart.getTime()) / (7 * 86_400_000));
    if (rule.frequency === "BIWEEKLY" && week % 2 !== 0) continue;
    if (!days.has(dayCodeOf(d))) continue;
    if (!take(d)) break;
  }
  return out;
}

/** Why the server would refuse this rule, or null when it is fine. Same rules,
 *  same order, as RecurrenceRule.Create and Expand. */
export function repeatProblem(start: string, rule: RepeatRule): string | null {
  const first = parseDay(start);
  if (!first) return null; // the date field reports its own error
  if (rule.end === "date") {
    const until = parseDay(rule.until);
    if (!until) return "Pick the date the series ends";
    if (until <= first) return "The series has to end after its first session";
    if ((until.getTime() - first.getTime()) / 86_400_000 > MAX_SPAN_DAYS) {
      return "A series can run for at most a year";
    }
  } else {
    const count = rule.count;
    if (count === undefined || !Number.isInteger(count) || count < 2 || count > MAX_SESSIONS) {
      return `A series has between 2 and ${MAX_SESSIONS} sessions`;
    }
  }
  const sessions = expandDates(start, rule);
  if (sessions.length > MAX_SESSIONS) {
    return `That makes more than ${MAX_SESSIONS} sessions. Pick an earlier end date.`;
  }
  if (sessions.length < 2) return "That schedule only has one session. Pick a later end date.";
  return null;
}
