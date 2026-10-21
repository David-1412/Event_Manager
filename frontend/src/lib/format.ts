import type { EventListItem, Tag } from "@/types/events";
import { availabilityOf } from "@/lib/cn";

/**
 * The browse preview's description excerpt. The panel is meant to be readable at
 * a glance, so it quotes roughly the first three lines rather than the whole
 * description, and marks the cut with an ellipsis.
 *
 * Truncating on a word boundary matters more than the exact budget: cutting at a
 * hard 280 characters reliably lands mid-word ("Bring a shin pa..."), which reads
 * as a bug. A word that alone overflows the budget is kept whole rather than
 * as a bug. A description that opens with a single word longer than the whole
 * budget is cut mid-word, since a truncated word still beats showing nothing.
 *
 * Blank/whitespace input yields "" so the caller can hide the block entirely
 * instead of rendering an empty paragraph with an ellipsis on it.
 */
export function truncateDescription(
  description: string | null | undefined,
  maxChars = 280,
): string {
  const text = description?.replace(/\s+/g, " ").trim();
  if (!text) return "";
  if (text.length <= maxChars) return text;

  const window = text.slice(0, maxChars + 1);
  const lastSpace = window.lastIndexOf(" ");
  // No space in the window means the first word alone overflows the budget. Cut
  // it rather than dropping the description or emitting an unbroken 400-character
  // line; the ellipsis at least promises the rest of the word is coming.
  const cut = lastSpace > 0 ? window.slice(0, lastSpace) : text.slice(0, maxChars);
  return `${cut.replace(/[,;:.\-–—]$/, "")}\u2026`;
}


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
/**
 * How long ago something was posted, the way a feed says it: `just now`, `5m`,
 * `3h`, `2d`, then the date once it is over a week old (`12 Oct`). A future time
 * (a skewed client clock) reads as `just now` rather than a negative age.
 */
export function formatTimeAgo(value: string | Date, now = new Date()): string {
  const date = toDate(value);
  if (!date) return "—";
  const seconds = Math.floor((now.getTime() - date.getTime()) / 1000);
  if (seconds < 60) return "just now";
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes}m`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}h`;
  const days = Math.floor(hours / 24);
  if (days < 7) return `${days}d`;
  return formatDayMonth(date);
}

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

/**
 * Capacity, where null means the host set no limit.
 *
 * Every spots display funnels through these three functions so "no limit" reads
 * identically on a card, a pin, and the detail page — a per-site ternary is how
 * one feature ends up with five different phrasings.
 */

/** `3 of 4 spots taken`; an unlimited event says how many came without implying a
 *  ceiling, because "3 of unlimited spots" is not a thing you can fill. */
export function spotsTakenSentence(current: number, max: number | null): string {
  return max === null
    ? `${current} ${current === 1 ? "person" : "people"} joined`
    : `${current} of ${max} spots taken`;
}

/** Remaining seats, or null when there is no limit. Null is returned rather than
 *  Infinity so callers cannot accidentally render "Infinity spots left" or compare
 *  against a number that is not a capacity. */
/** An event's capacity, or null when it has none. A missing value and 0 both mean
 *  "no limit" (0 is not a valid capacity - the API's floor is 2), so every display
 *  reads this instead of the raw field and can never print "null of null". */
export function capacityLimit(max: number | null | undefined): number | null {
  return typeof max === "number" && max > 0 ? max : null;
}

export function spotsLeft(current: number, max: number | null): number | null {
  return max === null ? null : Math.max(0, max - current);
}

/** `2 of 4 spots taken` for the detail page. */
export function formatSpotsTaken(current: number, max: number | null): string {
  return spotsTakenSentence(current, max);
}

/** The compact `4/10` on a card or map pin. With no limit there is no denominator
 *  to show, so the joined count stands alone rather than as `4/—`. */
export function formatSpotsRatio(current: number, max: number | null): string {
  return max === null ? `${current}` : `${current}/${max}`;
}

/** Whether an event can still take someone — the one place "full" is decided, so
 *  an unlimited event can never be reported full by one caller and not another. */
export function isFull(current: number, max: number | null): boolean {
  return availabilityOf(current, max) === "full";
}

/** Short capacity label for the admin list: `12 spots`, or `No limit`. */
export function formatCapacity(max: number | null): string {
  return max === null ? "No limit" : `${max} spots`;
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
