import type { SkillLevel, Tag } from "@/types/events";

/**
 * Tag rules mirrored from backend TagNormalizer. The client normalizes so the
 * chips it renders match what the server will store; the server is still the
 * authority and rejects anything that does not survive the same function.
 */
export const TAG_MAX_PER_EVENT = 5;
export const TAG_MAX_LENGTH = 25;
export const TAG_MIN_LENGTH = 2;

/** Lowercase, strip a leading '#', collapse internal whitespace, trim.
 * null when the result is not a usable tag (too short/long, or no letters or
 * digits), which is how a stray "#" never reaches the tags table. */
export function normalizeTag(raw: string | null | undefined): Tag | null {
  const trimmed = (raw ?? "").trim().replace(/^#+/, "").trim();
  if (!trimmed) return null;

  const collapsed = trimmed.split(/\s+/).join(" ").toLowerCase();
  if (collapsed.length < TAG_MIN_LENGTH || collapsed.length > TAG_MAX_LENGTH) {
    return null;
  }

  return /[\p{L}\p{N}]/u.test(collapsed) ? collapsed : null;
}

/** Normalize, drop invalid, dedupe first-seen, cap. Order preserved so the chip
 * order a user built survives a round trip rather than coming back sorted. */
export function normalizeTags(raws: readonly string[], max = TAG_MAX_PER_EVENT): Tag[] {
  const seen = new Set<string>();
  const out: Tag[] = [];
  for (const raw of raws) {
    const tag = normalizeTag(raw);
    if (!tag || seen.has(tag)) continue;
    seen.add(tag);
    out.push(tag);
    if (out.length >= max) break;
  }
  return out;
}

export const SKILL_LEVELS: readonly SkillLevel[] = [
  "Beginner",
  "Intermediate",
  "Advanced",
];

/** Melbourne CBD fallback when geolocation is denied (spec §8). */
export const MELBOURNE_CBD = { latitude: -37.8136, longitude: 144.9631 };

export const RADIUS_OPTIONS = [2, 5, 10, 25, 50] as const;

export const MAX_AVATAR_STACK = 4;

