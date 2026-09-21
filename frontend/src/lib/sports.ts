import type { SkillLevel, SportKey } from "@/types/events";

/**
 * Fallback only. `sportIcon` is server-supplied (spec §12); this map exists so
 * offline dev and the style guide render identically to production.
 */
export const SPORTS: ReadonlyArray<{
  key: SportKey;
  label: string;
  icon: string;
}> = [
  { key: "badminton", label: "Badminton", icon: "🏸" },
  { key: "basketball", label: "Basketball", icon: "🏀" },
  { key: "running", label: "Running", icon: "🏃" },
  { key: "soccer", label: "Soccer", icon: "⚽" },
  { key: "tennis", label: "Tennis", icon: "🎾" },
  { key: "cricket", label: "Cricket", icon: "🏏" },
  { key: "netball", label: "Netball", icon: "🥎" },
  { key: "volleyball", label: "Volleyball", icon: "🏐" },
];

export const SKILL_LEVELS: readonly SkillLevel[] = [
  "Beginner",
  "Intermediate",
  "Advanced",
];

const BY_KEY = new Map(SPORTS.map((s) => [s.key, s]));

export function sportMeta(sport: SportKey): { label: string; icon: string } {
  const found = BY_KEY.get(sport);
  return found ?? { label: sport, icon: "🏅" };
}

export function iconFor(sport: SportKey, serverIcon: string | null): string {
  return serverIcon ?? sportMeta(sport).icon;
}

/** Melbourne CBD fallback when geolocation is denied (spec §8). */
export const MELBOURNE_CBD = { latitude: -37.8136, longitude: 144.9631 };

export const RADIUS_OPTIONS = [2, 5, 10, 25, 50] as const;

export const MAX_AVATAR_STACK = 4;
