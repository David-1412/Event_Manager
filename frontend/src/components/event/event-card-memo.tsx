"use client";

import { memo } from "react";
import { EventCard } from "@/components/event/event-card";
import { CardActions } from "@/components/event/card-actions";
import type { CardProps } from "@/components/event/browse-layout";

interface EventCardMemoProps {
  /** The picked fields the card reads, not the feed row itself. */
  data: CardProps;
  selected: boolean;
  isJoined: boolean;
  onSelect: (id: string) => void;
  onOpen: (id: string) => void;
  density?: "default" | "compact";
}

/**
 * `EventCard` with the re-render discipline the browse list needs.
 *
 * Two things defeat a plain `memo(<EventCard event={row} ... />)` here:
 *
 * 1. The feed is re-fetched on a poll/optimistic interval and hands back rows
 *    that are structurally identical but referentially new, so `event` fails
 *    comparison on every tick and all ~100 cards re-render. The caller therefore
 *    passes picked fields (see `cardPropsOf`) rather than the row.
 * 2. The Interested/Join controls are a React element, and a fresh element is
 *    never `===` to the last one, which alone would defeat memo completely. So
 *    the element is built *inside* this boundary from `data`'s own fields, and
 *    the comparison never has to look at it.
 *
 * Together these are what keep a selection from re-rendering the whole list: the
 * selected card and the one that was selected before it change, the rest do not.
 */
export const EventCardMemo = memo(
  function EventCardMemo({
    data,
    selected,
    isJoined,
    onSelect,
    onOpen,
    density = "compact",
  }: EventCardMemoProps) {
    return (
      <EventCard
        event={data}
        selected={selected}
        isJoined={isJoined}
        density={density}
        onSelect={onSelect}
        onOpen={onOpen}
        action={
          <CardActions
            eventId={data.id}
            isCancelled={data.isCancelled}
            requiresJoinApproval={data.requiresJoinApproval}
          />
        }
      />
    );
  },
  // Default shallow comparison is right for everything except `data`, which is a
  // freshly-built object whenever the feed row it came from changed. Comparing the
  // fields the card actually renders makes the check about content, not identity.
  (prev, next) =>
    prev.selected === next.selected &&
    prev.isJoined === next.isJoined &&
    prev.onSelect === next.onSelect &&
    prev.onOpen === next.onOpen &&
    prev.density === next.density &&
    sameCardData(prev.data, next.data),
);


/**
 * Field-wise equality over the picked card fields.
 *
 * `tags` is the only collection and the only one compared by contents: the feed
 * view re-aggregates it on every query, so a same-looking row arrives with a new
 * array each time. Everything else is a primitive.
 */
function sameCardData(a: CardProps, b: CardProps): boolean {
  if (a === b) return true;
  for (const key of Object.keys(a) as (keyof CardProps)[]) {
    if (key === "tags") continue;
    if (a[key] !== b[key]) return false;
  }
  const at = a.tags;
  const bt = b.tags;
  if (at.length !== bt.length) return false;
  for (let i = 0; i < at.length; i++) if (at[i] !== bt[i]) return false;
  return true;
}
