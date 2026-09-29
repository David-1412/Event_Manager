import type { EventListItem, Tag } from "@/types/events";

/**
 * The only place date/time/cost/distance/tag strings are built (spec principle 6).
 * Everything is Australia/Melbourne-aware and never emits `Invalid Date`.
 */

/**
 * Tags as a compact card line: "#tennis · #club". Empty string when the event has
 * no tags, which is the normal state for events created before tags existed and
 * for every demo event (the seed creates none) - callers rely on this being
 * whitespace-safe rather than rendering "null" or a bare separator.
 */
export function formatTagLine(tags: readonly Tag[] | null | undefined, max = 3): string {
  if (!tags || tags.length === 0) return "";
  const shown = tags.slice(0, max).map((t) => `#${t}`);
  const extra = tags.length - shown.length;
  return extra > 0 ? `${shown.join(" · ")} +${extra}` : shown.join(" · ");
}


const TIME_FMT = new Intl.DateTimeFormat("en-AU", {
  hour: "numeric",
  minute: "2-digit",
  timeZone: "Australia/Melbourne",
});

const DAY_MONTH_FMT = new Intl.DateTimeFormat("en-AU", {
  weekday: "short",
  day: "numeric",
  month: "short",
  timeZone: "Australia/Melbourne",
});

const FULL_DATE_FMT = new Intl.DateTimeFormat("en-AU", {
  weekday: "long",
  day: "numeric",
  month: "long",
  timeZone: "Australia/Melbourne",
});

const HOUR_FMT = new Intl.DateTimeFormat("en-AU", {
  hour: "numeric",
  hour12: false,
  timeZone: "Australia/Melbourne",
});

/**
 * `formatToParts` rather than `format` because the ICU shape of en-AU is not the
 * shape the design asks for: ICU yields `6:00 pm`, the spec wants `6PM`. Reading
 * the parts lets us drop `:00`, fold the space and upper-case the meridiem
 * without a regex guess at a locale string.
 */
function clockParts(date: Date): { hour: string; minute: string; dayPeriod: string } {
  const values = TIME_FMT.formatToParts(date);
  const get = (type: Intl.DateTimeFormatPartTypes) =>
    values.find((part) => part.type === type)?.value ?? "";
  return {
    hour: get("hour").replace(/^0/, ""),
    minute: get("minute"),
    dayPeriod: get("dayPeriod").toUpperCase(),
  };
}

function parts(date: Date) {
  const values = new Intl.DateTimeFormat("en-AU", {
    timeZone: "Australia/Melbourne",
    weekday: "short",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).formatToParts(date);
  const get = (t: string) => values.find((p) => p.type === t)?.value ?? "00";
  return { key: `${get("year")}-${get("month")}-${get("day")}` };
}

function toDate(value: string | Date): Date | null {
  const date = value instanceof Date ? value : new Date(value);
  return Number.isNaN(date.getTime()) ? null : date;
}

export function isValidDate(value: string | Date): boolean {
  return toDate(value) !== null;
}

/** Melbourne calendar day, ignoring the wall time. */
export function dayKeyInMelbourne(value: string | Date, now = new Date()): string {
  return parts(toDate(value) ?? now).key;
}

/** `6PM` / `6:30PM` - Melbourne-aware, no `:00` noise (spec §0.6, §12). */
export function formatTime(value: string | Date): string {
  const date = toDate(value);
  if (!date) return "\u2014";
  const { hour, minute, dayPeriod } = clockParts(date);
  return minute === "00" ? `${hour}${dayPeriod}` : `${hour}:${minute}${dayPeriod}`;
}

/** `Mon 12 Oct` in Melbourne terms. ICU inserts `Mon, 12 Oct`; the comma goes. */
export function formatDayMonth(value: string | Date): string {
  const date = toDate(value);
  if (!date) return "\u2014";
  return DAY_MONTH_FMT.format(date).replace(",", "");
}

/** `Monday 12 October` for the detail page. */
export function formatFullDate(value: string | Date): string {
  const date = toDate(value);
  if (!date) return "\u2014";
  return FULL_DATE_FMT.format(date).replace(",", "");
}

export function isToday(value: string | Date, now = new Date()): boolean {
  const date = toDate(value);
  if (!date) return false;
  return parts(date).key === parts(now).key;
}

export function isTomorrow(value: string | Date, now = new Date()): boolean {
  const date = toDate(value);
  if (!date) return false;
  const next = new Date(now);
  next.setDate(next.getDate() + 1);
  return parts(date).key === parts(next).key;
}


/**
 * `Tonight 6PM` / `Tomorrow 6AM` / `Mon 12 Oct 6PM`.
 * `Tonight` only while the Melbourne clock is still 17:00+ on that same day.
 */
export function formatRelativeDayTime(
  value: string | Date,
  now = new Date(),
): string {
  const date = toDate(value);
  if (!date) return "—";
  const time = formatTime(date);
  if (isToday(date, now)) {
    return Number(HOUR_FMT.format(date)) >= 17
      ? `Tonight ${time}`
      : `Today ${time}`;
  }
  if (isTomorrow(date, now)) return `Tomorrow ${time}`;
  return `${formatDayMonth(date)} ${time}`;
}

/**
 * `6\u20138PM`, `6:15\u20138:30PM`, `6PM\u20131AM` (crossing midnight), `\u2014` when invalid.
 * Built from the same clock parts as `formatTime` so the two can never drift.
 */
export function formatTimeRange(start: string | Date, end: string | Date): string {
  const startAt = toDate(start);
  const endAt = toDate(end);
  if (!startAt || !endAt) return "\u2014";

  const from = clockParts(startAt);
  const to = clockParts(endAt);
  const startText = formatTime(startAt);
  const endText = formatTime(endAt);

  const sameMeridiem = from.dayPeriod === to.dayPeriod;
  if (!sameMeridiem) return `${startText}\u2013${endText}`;

  // Drop the shared meridiem from the left side: `6` + `\u2013` + `8PM`.
  const left = `${from.hour}${from.minute === "00" ? "" : `:${from.minute}`}`;
  return `${left}\u2013${endText}`;
}

/**
 * Card meta line (`Tonight 6\u20138PM`, `Mon 12 Oct 6:15\u20138:30PM`).
 * Composes the helpers above so no component does its own date maths.
 */
export function formatCardWhen(
  event: Pick<EventListItem, "startAt" | "endAt">,
  now = new Date(),
): string {
  if (!isValidDate(event.startAt) || !isValidDate(event.endAt)) return "\u2014";
  const date = new Date(event.startAt);
  const range = formatTimeRange(event.startAt, event.endAt);
  if (isToday(date, now)) {
    return Number(HOUR_FMT.format(date)) >= 17
      ? `Tonight ${range}`
      : `Today ${range}`;
  }
  if (isTomorrow(date, now)) return `Tomorrow ${range}`;
  return `${formatDayMonth(date)} ${range}`;
}

/** `Free` for null/0 (spec §7 card states). `$15`, `$12.50`. */
export function formatCost(cost: number | null): string {
  if (cost === null || cost === undefined || cost === 0) return "Free";
  return new Intl.NumberFormat("en-AU", {
    style: "currency",
    currency: "AUD",
    minimumFractionDigits: Number.isInteger(cost) ? 0 : 2,
    maximumFractionDigits: 2,
  }).format(cost);
}

/** `3 km away`, `320 m away`, `On-site`; unknown distance is `Nearby`. */
export function formatDistance(distanceKm: number | null): string {
  if (distanceKm === null || distanceKm === undefined) return "Nearby";
  if (distanceKm < 0.1) return "On-site";
  if (distanceKm < 1) return `${Math.round(distanceKm * 1000)} m away`;
  const rounded =
    distanceKm < 10 ? Math.round(distanceKm * 10) / 10 : Math.round(distanceKm);
  return `${rounded} km away`;
}

/** Screen-reader sentence for the count live region (spec §7). */
export function spotsTakenSentence(current: number, max: number): string {
  return `${current} of ${max} spots taken`;
}

export function spotsLeft(current: number, max: number): number {
  return Math.max(0, max - current);
}

/** `2 of 4 spots taken` for the detail page. */
export function formatSpotsTaken(current: number, max: number): string {
  return spotsTakenSentence(current, max);
}

/** `YYYY-MM-DD` for `<input type="date">` values and minima, in Melbourne. */
export function melbourneDateInputValue(value = new Date()): string {
  return parts(value).key;
}

/**
 * The Melbourne wall-clock `HH:mm` a native time input shows for an instant —
 * the exact inverse of the `date`+`startTime` strings `toCreateEventPayload`
 * reads back. Draft review needs it: a payload stores ISO instants, the form
 * edits wall-clock strings, and getting the pair wrong shifts every extracted
 * time by the zone offset.
 */
export function melbourneTimeInputValue(value: string | Date): string {
  const at = new Date(value);
  if (!isValidDate(at)) return "";
  const parts = new Intl.DateTimeFormat("en-AU", {
    hour: "2-digit",
    minute: "2-digit",
    hour12: false,
    timeZone: "Australia/Melbourne",
  }).formatToParts(at);
  const get = (type: Intl.DateTimeFormatPartTypes) =>
    parts.find((part) => part.type === type)?.value ?? "";
  const hour = get("hour");
  return `${hour === "24" ? "00" : hour}:${get("minute")}`;
}
