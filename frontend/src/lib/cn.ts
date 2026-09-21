import { clsx, type ClassValue } from "clsx";

/** Single class joiner so components never hand-roll conditional class strings. */
export function cn(...inputs: ClassValue[]): string {
  return clsx(inputs);
}

export type Availability = "open" | "one-spot" | "full";

/**
 * Availability is the only semantic colour in the system (spec §2):
 * `>= 2` open = brand, `1` open = warn, `0` open = danger.
 */
export function availabilityOf(
  current: number,
  max: number,
): Availability {
  const left = Math.max(0, max - current);
  if (left === 0) return "full";
  if (left === 1) return "one-spot";
  return "open";
}

export const countTextClass: Record<Availability, string> = {
  open: "text-fg",
  "one-spot": "text-warn",
  full: "text-danger",
};

/** Clamp helper for optimistic counters so the UI can never show 5/4. */
export function clampCount(value: number, max: number): number {
  return Math.min(Math.max(value, 0), max);
}

/** Deterministic initials for the Avatar fallback (never a broken image). */
export function initialsOf(name: string | null | undefined): string {
  const trimmed = (name ?? "").trim();
  if (!trimmed) return "?";
  const words = trimmed.split(/\s+/);
  const first = words[0]?.[0] ?? "";
  const second = words.length > 1 ? (words.at(-1)?.[0] ?? "") : "";
  return (first + second).toUpperCase().slice(0, 2);
}

export function pluralise(count: number, singular: string, plural?: string) {
  return count === 1 ? singular : (plural ?? `${singular}s`);
}
