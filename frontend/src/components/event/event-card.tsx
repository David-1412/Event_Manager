import Link from "next/link";
import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/cn";
import { availabilityOf, countTextClass } from "@/lib/cn";
import {
  formatCardWhen,
  formatCost,
  formatDistance,
  formatTagLine,
  spotsTakenSentence,
} from "@/lib/format";
import type { EventListItem } from "@/types/events";

export type EventCardVariant =
  | "open"
  | "one-spot"
  | "full"
  | "joined"
  | "mine"
  | "cancelled"
  | "past";

export interface EventCardProps {
  event: EventListItem;
  /** Derived by the caller when the viewer's relation is known. */
  isJoined?: boolean;
  isHost?: boolean;
  variant?: EventCardVariant;
  /** Compact single line used by `Past` on /my-events (spec §8). */
  onSelect?: (id: string) => void;
  selected?: boolean;
  /** Rendered in the footer slot; on `/` this is the JoinButton. */
  action?: React.ReactNode;
}

/**
 * Variant priority (spec §7): cancelled > mine (hosting) > joined > full >
 * one-spot > open. `past` is only ever passed explicitly by /my-events.
 */
export function eventCardVariant(
  event: EventListItem,
  flags: { isJoined?: boolean; isHost?: boolean } = {},
): EventCardVariant {
  if (event.isCancelled) return "cancelled";
  if (flags.isHost) return "mine";
  if (flags.isJoined) return "joined";
  const availability = availabilityOf(event.participantCount, event.maxParticipants);
  if (availability === "full") return "full";
  if (availability === "one-spot") return "one-spot";
  return "open";
}

export function EventCard({
  event,
  isJoined = false,
  isHost = false,
  variant,
  selected = false,
  action,
  onSelect,
}: EventCardProps) {
  const kind = variant ?? eventCardVariant(event, { isJoined, isHost });
  const availability = availabilityOf(event.participantCount, event.maxParticipants);
  // Sport is decoration only now: null when the event has no sport row, and the
  // glyph slot collapses rather than falling back to a generic emoji.
  const icon = event.sportIcon;
  const tagLine = formatTagLine(event.tags);


  if (kind === "past") {
    return (
      <Link
        href={`/events/${event.id}`}
        className="press flex items-center gap-2 rounded-md border border-border bg-surface px-4 py-3 text-meta text-fg-muted no-underline hover:bg-surface-2"
      >
        <span aria-hidden>{icon}</span>
        <span className="truncate">
          {tagLine} · {formatCardWhen(event)}
        </span>
        {/* reserved slot for the post-MVP rating control (spec §8) */}
        <span aria-hidden className="ml-auto text-micro uppercase">
          Rate
        </span>
      </Link>
    );
  }

  const countLabel = spotsTakenSentence(event.participantCount, event.maxParticipants);
  const meta = [
    formatCardWhen(event),
    formatDistance(event.distanceKm),
    formatCost(event.cost),
  ].join(" · ");

  return (
    <article
      className={cn(
        "press group relative rounded-md border border-border bg-surface p-4",
        // hover lifts by translation only — `scale` would reflow the grid (spec §7)
        "hover:-translate-y-px hover:shadow-raise",
        kind === "mine" && "border-l-[3px] border-l-brand-600",
        kind === "cancelled" && "bg-surface-2 text-fg-muted hover:translate-y-0 hover:shadow-none",
        kind === "joined" && "bg-surface",
        selected && "border-brand-600 ring-1 ring-brand-600",
      )}
      aria-label={`${event.title}, ${countLabel}`}
      onClick={onSelect ? () => onSelect(event.id) : undefined}
    >
      <div className="flex items-center gap-2 text-meta text-fg-muted">
        {icon && <span aria-hidden>{icon}</span>}
        <span className="truncate">{tagLine}</span>
        {/* SkillLevel is nullable and the create form no longer collects it, so a
            null renders nothing at all rather than an empty pill. */}
        {event.skillLevel && <Badge className="ml-auto">{event.skillLevel}</Badge>}
      </div>


      <h3 className="mt-2 text-h3 text-fg">
        <Link
          href={`/events/${event.id}`}
          className="no-underline after:absolute after:inset-0 after:content-[''] focus:outline-hidden"
        >
          {event.title}
        </Link>
      </h3>

      <p className="mt-1 text-meta text-fg-muted">{event.venueName}</p>
      <p className="mt-0.5 text-meta text-fg-muted">{meta}</p>

      <div
        className={cn(
          "mt-4 flex flex-wrap items-center gap-2",
          kind === "joined" && "-mx-4 -mb-4 rounded-b-md border-t border-border bg-brand-tint px-4 py-3",
        )}
      >
        <Count event={event} kind={kind} availability={availability} />
        {kind === "one-spot" && <Badge tone="warn">1 spot left</Badge>}
        {kind === "full" && <Badge tone="danger">Full</Badge>}
        {kind === "mine" && <Badge tone="brand">You&apos;re hosting</Badge>}
        {kind === "cancelled" && <Badge tone="info">Cancelled</Badge>}
        {action && (
          <span
            className="relative z-10 ml-auto"
            onClick={(e) => e.stopPropagation()}
            onKeyDown={(e) => e.stopPropagation()}
          >
            {action}
          </span>
        )}
      </div>
    </article>
  );
}

/** The hero number (spec principle 1): biggest thing on the card, colour-coded. */
function Count({
  event,
  kind,
  availability,
}: {
  event: EventListItem;
  kind: EventCardVariant;
  availability: "open" | "one-spot" | "full";
}) {
  const tone =
    kind === "cancelled"
      ? "text-fg-muted"
      : kind === "full"
        ? "text-danger"
        : countTextClass[availability];
  return (
    <span className={cn("count-shift text-count", tone)}>
      <span data-count>
        {event.participantCount}/{event.maxParticipants}
      </span>
      <span className="sr-only"> — {spotsTakenSentence(event.participantCount, event.maxParticipants)}</span>
    </span>
  );
}
