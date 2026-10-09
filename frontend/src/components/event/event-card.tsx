"use client";

import Link from "next/link";
import { useState } from "react";
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
import { isPendingReviewStatus } from "@/types/events";

export type EventCardVariant =
  | "open"
  | "one-spot"
  | "full"
  | "joined"
  | "mine"
  | "cancelled"
  | "ended"
  | "past"
  | "pending-review";

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
  /** Hide the poster/image area. The create-event live preview uses this so the
   *  card shows only the text fields; browse cards keep the image (default). */
  hideImage?: boolean;
}

/**
 * Variant priority (spec §7): cancelled > pending-review > mine (hosting) >
 * joined > full > one-spot > open. `past` is only ever passed explicitly by /my-events.
 *
 * Pending-review outranks `mine` deliberately. A public event a regular user
 * created is not live yet, so rendering it as "You're hosting" with open
 * availability would advertise slots for something nobody can find or join.
 */
export function eventCardVariant(
  event: EventListItem,
  flags: { isJoined?: boolean; isHost?: boolean } = {},
): EventCardVariant {
  if (event.isCancelled) return "cancelled";
  if (isPendingReviewStatus(event.status)) return "pending-review";
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
  hideImage = false,
}: EventCardProps) {
  const kind = variant ?? eventCardVariant(event, { isJoined, isHost });
  const availability = availabilityOf(event.joinedCount, event.maxParticipants);
  // Sport is decoration only now: null when the event has no sport row, and the
  // glyph slot collapses rather than falling back to a generic emoji.
  const icon = event.sportIcon;
  const tagLine = formatTagLine(event.tags);
  // Resolve the stored /uploads path to the API origin. null (no image, or a
  // failed load) drops the poster to its placeholder in the SAME slot, so the
  // card keeps its size and never jumps (no layout shift, no broken-image box).
  const [thumbFailed, setThumbFailed] = useState(false);
  const posterSrc = thumbFailed ? null : mediaUrl(event.thumbnailUrl);

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
  // The when-line is rendered separately in brand colour (see below) so the time
  // reads as the card's key fact; distance and cost stay in the muted meta line.
  const meta = [formatDistance(event.distanceKm), formatCost(event.cost)].join(" · ");

  return (
    <article
      className={cn(
        // Poster-first card: a column on mobile (poster on top, details below),
        // a row at >=768px (details left, poster a fixed-width full-height
        // column on the right). `overflow-hidden` clips the poster's hover zoom
        // and the placeholder art to the card's rounded corners, so the poster
        // always bleeds flush to the edges instead of floating inside padding.
        "press group relative flex flex-col overflow-hidden rounded-md border border-border bg-surface md:flex-row",
        // hover lifts by translation only — `scale` would reflow the grid (spec §7)
        "hover:-translate-y-0.5 hover:shadow-raise transition-[transform,box-shadow] duration-200",
        kind === "mine" && "border-l-[3px] border-l-brand-600",
        // Not muted like cancelled/ended: a pending event is live-in-waiting, not
        // dead. The left accent carries its state the way `mine` does.
        kind === "pending-review" && "border-l-[3px] border-l-warn",
        kind === "cancelled" && "bg-surface-2 text-fg-muted hover:translate-y-0 hover:shadow-none",
        kind === "ended" && "bg-surface-2 hover:translate-y-0 hover:shadow-none",
        kind === "joined" && "bg-surface",
        selected && "border-brand-600 ring-1 ring-brand-600",
      )}
      aria-label={`${event.title}, ${countLabel}`}
      onClick={onSelect ? () => onSelect(event.id) : undefined}
    >
      {/* Details first so they read first; on mobile the poster is pushed to the
          top with `order-first`, on desktop it sits on the right. */}
      <div className="flex min-w-0 flex-1 flex-col gap-1 p-4">
      <div className="flex items-center gap-2 text-meta text-fg-muted">
        {icon && <span aria-hidden>{icon}</span>}
        <span className="truncate">{tagLine}</span>
        {/* SkillLevel is nullable and the create form no longer collects it, so a
            null renders nothing at all rather than an empty pill. */}
        {event.skillLevel && <Badge className="ml-auto">{event.skillLevel}</Badge>}
      </div>


      <h3 className="text-h3 text-fg">
        <Link
          href={`/events/${event.id}`}
          className="no-underline after:absolute after:inset-0 after:content-[''] focus:outline-hidden"
        >
          {event.title}
        </Link>
      </h3>

      <p className="text-meta text-fg-muted">{event.venueName}</p>
      <p className="text-meta font-semibold text-brand-600">{formatCardWhen(event)}</p>
      <p className="text-meta text-fg-muted">{meta}</p>

      <div
        className={cn(
          // mt-auto pins the count + actions to the bottom of the details
          // column, so they sit bottom-left regardless of how tall the poster
          // makes the card.
          "mt-auto flex flex-wrap items-center gap-2 pt-4",
          kind === "joined" && "-mx-4 -mb-4 rounded-b-md border-t border-border bg-brand-tint px-4 py-3",
        )}
      >
        <Count event={event} kind={kind} availability={availability} />
        <Tally event={event} />
        {kind === "one-spot" && <Badge tone="warn">1 spot left</Badge>}
        {kind === "full" && <Badge tone="danger">Full</Badge>}
        {kind === "mine" && <Badge tone="brand">You&apos;re hosting</Badge>}
        {kind === "pending-review" && <Badge tone="warn">Pending Approval</Badge>}
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
      {!hideImage && (
        <Poster src={posterSrc} icon={icon} failed={thumbFailed} onError={() => setThumbFailed(true)} />
      )}
    </article>
  );
}

/**
 * The poster panel — the card's dominant visual. On mobile a full-width banner
 * above the details; at >=768px a fixed 280px column (roughly a third of the
 * card) that fills the card's full
 * height on the right. `object-cover` crops any source ratio (portrait,
 * landscape, square) to fill without stretching, and the group-hover scale(1.03)
 * is clipped by the card's `overflow-hidden`. With no image (or a failed load)
 * it keeps the exact same footprint and shows a placeholder, so the layout never
 * shifts between the two states.
 */
function Poster({
  src,
  icon,
  failed,
  onError,
}: {
  src: string | null;
  icon: string | null;
  failed: boolean;
  onError: () => void;
}) {
  return (
    <div
      aria-hidden
      className="relative order-first h-48 w-full shrink-0 overflow-hidden bg-surface-2 md:order-none md:h-auto md:w-70"
    >
      {src ? (
        // Plain <img>, not next/image: the bytes come from the API origin, which
        // isn't a configured image domain, and these are the host's own files.
        // alt="" - the title names the event; the image is decoration.
        // eslint-disable-next-line @next/next/no-img-element
        <img
          src={src}
          alt=""
          onError={onError}
          className="h-full w-full object-cover transition-transform duration-300 ease-out group-hover:scale-[1.03]"
        />
      ) : (
        <div className="flex h-full w-full items-center justify-center">
          {failed ? (
            <span className="text-h1 opacity-40" aria-hidden>
              {"\u{1F5BC}"}
            </span>
          ) : (
            <>
              <span className="absolute inset-0 bg-[radial-gradient(circle_at_30%_20%,var(--brand),transparent_60%)] opacity-15" />
              {icon && (
                <span className="relative text-4xl opacity-60" aria-hidden>
                  {icon}
                </span>
              )}
            </>
          )}
        </div>
      )}
    </div>
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
    kind === "cancelled" || kind === "pending-review"
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
