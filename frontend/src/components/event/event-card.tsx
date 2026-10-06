import Link from "next/link";
import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/cn";
import { availabilityOf, countTextClass } from "@/lib/cn";
import { mediaUrl } from "@/lib/api";
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
  | "ended"
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
  if (event.status === "Completed") return "ended";
  if (flags.isHost) return "mine";
  if (flags.isJoined) return "joined";
  const availability = availabilityOf(event.joinedCount, event.maxParticipants);
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
  const availability = availabilityOf(event.joinedCount, event.maxParticipants);
  // Sport is decoration only now: null when the event has no sport row, and the
  // glyph slot collapses rather than falling back to a generic emoji.
  const icon = event.sportIcon;
  const tagLine = formatTagLine(event.tags);
  // Resolve the stored /uploads path to the API origin, or null when there is no
  // image - the card then renders exactly as before, with no image area at all.
  const thumbnailSrc = mediaUrl(event.thumbnailUrl);


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

  const countLabel = spotsTakenSentence(event.joinedCount, event.maxParticipants);
  const meta = [
    formatCardWhen(event),
    formatDistance(event.distanceKm),
    formatCost(event.cost),
  ].join(" · ");

  return (
    <article
      className={cn(
        "press group relative rounded-md border border-border bg-surface p-4",
        // With a thumbnail the card splits into a row at >=768px: content fills the
        // space beside a fixed-width image, and the card padding comes off so the
        // image can bleed to the edge. Without one it stays a plain padded column.
        thumbnailSrc && "md:flex md:items-center md:gap-4 md:p-0",
        // hover lifts by translation only — `scale` would reflow the grid (spec §7)
        "hover:-translate-y-px hover:shadow-raise",
        kind === "mine" && "border-l-[3px] border-l-brand-600",
        kind === "cancelled" && "bg-surface-2 text-fg-muted hover:translate-y-0 hover:shadow-none",
        kind === "ended" && "bg-surface-2 hover:translate-y-0 hover:shadow-none",
        kind === "joined" && "bg-surface",
        selected && "border-brand-600 ring-1 ring-brand-600",
      )}
      aria-label={`${event.title}, ${countLabel}`}
      onClick={onSelect ? () => onSelect(event.id) : undefined}
    >
      <div className={cn("min-w-0", thumbnailSrc && "md:p-4")}>
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
        <Tally event={event} />
        {kind === "one-spot" && <Badge tone="warn">1 spot left</Badge>}
        {kind === "full" && <Badge tone="danger">Full</Badge>}
        {kind === "mine" && <Badge tone="brand">You&apos;re hosting</Badge>}
        {kind === "cancelled" && <Badge tone="info">Cancelled</Badge>}
        {kind === "ended" && <Badge tone="neutral">Ended</Badge>}
        {kind === "ended" && event.isCancelled && <Badge tone="info">Cancelled</Badge>}
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
      </div>
      {thumbnailSrc && (
        // Host-uploaded thumbnail. Plain <img> (not next/image): the bytes come
        // from the API origin, which isn't a configured image domain, and these
        // are the host's own files. alt="" - the title names the event; the image
        // is decoration. Below 768px it is a full-width banner bleeding to the
        // card's top edge; at >=768px the card is a row and it becomes a fixed
        // 160px column that fills the card height, vertically centred, rounded on
        // the card's right corners.
        // eslint-disable-next-line @next/next/no-img-element
        <img
          src={thumbnailSrc}
          alt=""
          aria-hidden
          className="-mx-4 -mt-4 mb-3 h-32 w-[calc(100%+2rem)] rounded-t-md object-cover md:mx-0 md:mt-0 md:mb-0 md:h-auto md:w-40 md:self-stretch md:rounded-l-none md:rounded-r-md"
        />
      )}
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
      <span data-count aria-hidden>
        {event.joinedCount}/{event.maxParticipants}
      </span>
      <span className="sr-only"> — {spotsTakenSentence(event.joinedCount, event.maxParticipants)}</span>
    </span>
  );
}

/**
 * The two count lines the browse card shows (spec): joined against capacity, and
 * interested as a standalone tally. Interest reserves no spot, so it is shown
 * beside — never summed into — the joined number.
 */
function Tally({ event }: { event: EventListItem }) {
  return (
    <span className="flex flex-col text-meta text-fg-muted leading-tight">
      <span>
        {"\u{1F465}"} {event.joinedCount}/{event.maxParticipants} joined
      </span>
      <span>
        {"\u2B50"} {event.interestedCount} interested
      </span>
    </span>
  );
}
