import type { EventListItem } from "@/types/events";

/**
 * How the browse list is laid out. Kept out of the URL on purpose: unlike the
 * filters, this is a display preference rather than a description of *which*
 * events are being looked at, so it must survive Back without becoming a history
 * entry, and `/` should never deep-link into grid mode.
 */
export type BrowseLayoutMode = "list" | "grid";

/**
 * How much of the viewport the map claims. Two states, not a boolean and not a
 * third "full" tier: the map column is either open beside the list or it is not.
 * A wider tier was removed because the map already fills its column top to
 * bottom - the only thing more width could buy was a narrower, unusable list.
 *
 * `none` is the default. The map used to hold 40% of the screen permanently,
 * which is 40% of the screen the majority of browsing never uses.
 */
export type BrowseMapMode = "none" | "split";

/**
 * Rows taller than this are windowed. Below it a plain map is cheaper than the
 * bookkeeping, and the fixed-height assumption the windowing rests on only
 * really holds once the list is long enough to scroll.
 */
export const VIRTUALIZE_THRESHOLD = 24;

/** True when the list is long enough to be worth windowing. */
export function shouldVirtualize(count: number): boolean {
  return count > VIRTUALIZE_THRESHOLD;
}

/**
 * The two columns' widths with the map open, as Tailwind fraction classes. An
 * even read: the map fills its half top to bottom, and the list keeps enough
 * width for the compact card's social-proof row to survive without truncating.
 */
export const SPLIT_LIST_WIDTH = "lg:w-1/2";
export const SPLIT_MAP_WIDTH = "lg:w-1/2";

/**
 * The subset of a row the list actually renders.
 *
 * `useMemo` over the whole row is not enough: the feed is re-fetched on a
 * poll/optimistic interval, and a refetch hands back rows that are structurally
 * identical but referentially new, which defeats memo on every tick and
 * re-renders all ~100 cards. Picking the fields the card reads gives the
 * comparison something stable to hold onto.
 *
 * `joinedCount` is in the list deliberately: joining must re-render that one card
 * immediately. `interestedCount` is too, since the same card shows it.
 */
export function cardPropsOf(event: EventListItem) {
  return {
    id: event.id,
    title: event.title,
    description: event.description ?? null,
    tags: event.tags,
    sportIcon: event.sportIcon,
    hostName: event.hostName ?? null,
    hostPhotoUrl: event.hostPhotoUrl ?? null,
    hostId: event.hostId ?? null,
    hostRole: event.hostRole ?? null,
    venueName: event.venueName,
    address: event.address ?? null,
    thumbnailUrl: event.thumbnailUrl ?? null,
    startAt: event.startAt,
    endAt: event.endAt,
    timezone: event.timezone,
    cost: event.cost,
    maxParticipants: event.maxParticipants,
    joinedCount: event.joinedCount,
    interestedCount: event.interestedCount,
    status: event.status,
    isCancelled: event.isCancelled,
    visibility: event.visibility,
    requiresJoinApproval: event.requiresJoinApproval ?? false,
    skillLevel: event.skillLevel ?? null,
    distanceKm: event.distanceKm ?? null,
    latitude: event.latitude,
    longitude: event.longitude,
  };
}

export type CardProps = ReturnType<typeof cardPropsOf>;

/**
 * The card data a memoised row is allowed to depend on. `null` for an id that is
 * not (or no longer) in the feed - the caller renders nothing for it rather than
 * a stale card.
 */
export function cardPropsById(items: readonly EventListItem[]): Map<string, CardProps | null> {
  const map = new Map<string, CardProps | null>();
  for (const item of items) map.set(item.id, cardPropsOf(item));
  return map;
}
