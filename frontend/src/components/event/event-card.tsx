"use client";

import Link from "next/link";
import { useState } from "react";
import { Badge } from "@/components/ui/badge";
import { HostLink } from "@/components/profile/host-link";
import { isCreatorRole } from "@/components/admin/role-badge";
import { cn } from "@/lib/cn";
import { availabilityOf, countTextClass, type Availability } from "@/lib/cn";
import { mediaUrl } from "@/lib/api";
import {
  formatCardWhen,
  formatCost,
  formatDistance,
  formatSpotsRatio,
  formatTagLine,
  spotsTakenSentence,
} from "@/lib/format";
import type { EventListItem } from "@/types/events";
import { isPendingReviewStatus } from "@/types/events";

export type EventCardVariant =
  | "open"
  | "one-spot"
  | "full"
  /** No capacity set. Its own variant rather than a fall-through to "open" so a
   * card cannot be styled as if it had a denominator it does not have. */
  | "unlimited"
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
  /**
   * Fires on the *first* activation: select the card without navigating.
   * When absent the card is a plain link and its title navigates directly -
   * that is the `/my-events` behaviour. When present the card takes on the
   * browse select-then-open model and `onOpen` decides where it goes.
   */
  onSelect?: (id: string) => void;
  /**
   * Fires on the *second* activation of an already-selected card, and is what
   * the caller uses to open the full event page. Required for `onSelect` to
   * mean anything - selecting with no way out would trap the user.
   */
  onOpen?: (id: string) => void;
  selected?: boolean;
  /** Rendered in the footer slot; on `/` this is the JoinButton. */
  action?: React.ReactNode;
  /** Hide the poster/image area. The create-event live preview uses this so the
   *  card shows only the text fields; browse cards keep the image (default). */
  hideImage?: boolean;
  /**
   * Layout density. `default` keeps the poster-first card - a 280px image column
   * at >=768px, a full-width banner below - which is what /my-events, the profile
   * page and the create preview have always rendered, and what they keep.
   *
 * `compact` is the browse card: the poster becomes a square thumbnail leading
 * the row (80px on mobile, 128px from `lg` up) and vertically centred against
 * the text, the social proof moves up under the title, and the vertical rhythm
 * tightens so a screenful still holds 6-8 events.
 *
 * The thumbnail stays smaller than the `default` poster on purpose. It is a row
 * leader, not a banner: row height is what a screenful of events is divided by,
 * so the image can only grow by making every card taller. 128px is where those
 * two pressures balance - big enough to recognise an event by its art at a
 * glance, small enough that the list still reads as a list.
   *
   * One component with a prop rather than a second card, because both must agree
   * on variant priority, activation semantics and every badge rule - exactly the
   * parts that drift when they get duplicated.
   */
  density?: EventCardDensity;
}

/** The two layouts `EventCard` can render. See `EventCardProps.density`. */
export type EventCardDensity = "default" | "compact";

/**
 * Variant priority (spec Â§7): cancelled > pending-review > mine (hosting) >
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
  if (availability === "unlimited") return "unlimited";
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
  onOpen,
  hideImage = false,
  density = "default",
}: EventCardProps) {
  const compact = density === "compact";
  const kind = variant ?? eventCardVariant(event, { isJoined, isHost });
  const availability = availabilityOf(event.joinedCount, event.maxParticipants);
  // Sport is decoration only now: null when the event has no sport row, and the
  // glyph slot collapses rather than falling back to a generic emoji.
  const icon = event.sportIcon;
  const tagLine = formatTagLine(event.tags);
  // Trimmed so a whitespace-only name is treated as no name, matching the preview
  // panel: an avatar of nothing is worse than no host line at all.
  const hostName = event.hostName?.trim() || null;
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
          {tagLine} Â· {formatCardWhen(event)}
        </span>
        {/* reserved slot for the post-MVP rating control (spec Â§8) */}
        <span aria-hidden className="ml-auto text-micro uppercase">
          Rate
        </span>
      </Link>
    );
  }

  const countLabel = spotsTakenSentence(event.joinedCount, event.maxParticipants);
  // The when-line is rendered separately in brand colour (see below) so the time
  // reads as the card's key fact; distance and cost stay in the muted meta line.
  const meta = [formatDistance(event.distanceKm), formatCost(event.cost)].join(" Â· ");

  // Two interaction models share this component.
  //
  // Browse (onSelect present): the card is a control. The first activation
  // selects - pans the map and opens the preview - and a second activation on
  // the already-selected card opens the full page. Nothing may navigate on that
  // first activation, which is why the title stops being a link here; see the
  // note on `titleIsPlain`.
  //
  // Everywhere else (onSelect absent): the title is a link with an inset
  // overlay, so the whole card is one anchor and needs no JS to work.
  const selectable = Boolean(onSelect);

  function activate() {
    if (selected) onOpen?.(event.id);
    else onSelect?.(event.id);
  }

  function onKeyDown(event: React.KeyboardEvent) {
    if (event.key !== "Enter" && event.key !== " ") return;
    // Space would otherwise scroll the list out from under the activation.
    event.preventDefault();
    activate();
  }

  return (
    <article
      // Browse makes the card a control, so it needs to be reachable and to say
      // what it is. `aria-pressed` rather than `aria-selected`: this is a toggle
      // in a list, not an option in a listbox, and the latter would promise
      // arrow-key navigation the card does not implement.
      role={selectable ? "button" : undefined}
      tabIndex={selectable ? 0 : undefined}
      aria-pressed={selectable ? selected : undefined}
      className={cn(
        // Poster-first card (default): a column on mobile (poster on top, details
        // below), a row at >=768px (details left, poster a fixed-width full-height
        // column on the right). `overflow-hidden` clips the poster's hover zoom
        // and the placeholder art to the card's rounded corners, so the poster
        // always bleeds flush to the edges instead of floating inside padding.
        //
        // Compact inverts that: the poster becomes a square thumbnail leading a
        // single row, vertically centred against the text block. It stays a row at
        // every width because the whole point is fitting more events in the
        // viewport, and a stacked banner would give that height straight back.
        cn(
          "press group relative flex flex-1 overflow-hidden rounded-md border border-border bg-surface",
          compact ? "flex-row p-3" : "flex-col md:flex-row",
        ),
        // hover lifts by translation only â€” `scale` would reflow the grid (spec Â§7)
        "hover:-translate-y-0.5 hover:shadow-raise transition-[transform,box-shadow] duration-200",
        kind === "mine" && "border-l-[3px] border-l-brand-600",
        // Not muted like cancelled/ended: a pending event is live-in-waiting, not
        // dead. The left accent carries its state the way `mine` does.
        kind === "pending-review" && "border-l-[3px] border-l-warn",
        kind === "cancelled" && "bg-surface-2 text-fg-muted hover:translate-y-0 hover:shadow-none",
        kind === "ended" && "bg-surface-2 hover:translate-y-0 hover:shadow-none",
        kind === "joined" && "bg-surface",
        // Selected: brand ring plus a tinted ground, so the highlighted card is
        // obvious against its neighbours without relying on colour alone - the
        // ring changes the border weight too.
        selected && "border-brand-600 bg-brand-tint ring-2 ring-brand-600",
        selectable && "focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-600",
      )}
      aria-label={`${event.title}, ${countLabel}`}
      onClick={selectable ? activate : undefined}
      onKeyDown={selectable ? onKeyDown : undefined}
    >
      {/* Details first so they read first; on mobile the poster is pushed to the
          top with `order-first`, on desktop it sits on the right. */}
      <div
        className={cn(
          "flex min-w-0 flex-1 flex-col",
          // gap-0.5 rather than the old gap-1: with a large image beside it the
          // text column reads as one block of tight lines, and a 4px gap between
          // each was most of what made the card look half-empty.
          //
          // No pl-3 in compact: the gap now comes from the thumbnail's own mr-3,
          // and carrying both made the text column's left inset depend on two
          // numbers that could drift apart.
          compact ? "gap-0.5" : "gap-1 p-4",
        )}
      >
      {!compact && (
        <div className="flex items-center gap-2 text-meta text-fg-muted">
          {icon && <span aria-hidden>{icon}</span>}
          <span className="truncate">{tagLine}</span>
          {/* SkillLevel is nullable and the create form no longer collects it, so a
              null renders nothing at all rather than an empty pill. */}
          {event.skillLevel && <Badge className="ml-auto">{event.skillLevel}</Badge>}
        </div>
      )}


      <h3 className={cn("text-fg", compact ? "text-body font-semibold" : "text-h3")}>
        {selectable ? (
          // Plain text, not a link: an inset link overlay would cover the card
          // and navigate on the first click. See the note in the heading.
          <span>{event.title}</span>
        ) : (
          <Link
            href={`/events/${event.id}`}
            className="no-underline after:absolute after:inset-0 after:content-[''] focus:outline-hidden"
          >
            {event.title}
          </Link>
        )}
      </h3>

      {compact ? (
        <>
          {/* Date first (visual hierarchy: it outranks location, price and skill),
              then location, then price/distance - each on its own line so the eye
              gets all three in one pass down the left edge. */}
          <p className="truncate text-meta font-semibold text-brand-600">
            {formatCardWhen(event)}
          </p>
          <p className="truncate text-meta text-fg-muted">
            <span aria-hidden>{"\u{1F4CD}"}</span> {event.venueName}
          </p>
          <p className="truncate text-meta text-fg-muted">{meta}</p>
          {/* Social proof under the title rather than in the footer, which is the
              difference between "3-4 events a screen" and "8-10": the numbers are
              what a student actually compares, so they sit within the first two
              lines of the card. */}
          <SocialProof event={event} availability={availability} />
        </>
      ) : (
        <>
          <p className="text-meta text-fg-muted">{event.venueName}</p>
          <p className="text-meta font-semibold text-brand-600">{formatCardWhen(event)}</p>
          <p className="text-meta text-fg-muted">{meta}</p>
        </>
      )}

      {/* Who runs it. The card deliberately did not name its host before - the
          browse card's comment on `hostName` records that only the admin queue needed
          it - but a card is now the entry point to "should I trust this host", and the
          name links to their profile.
          Absent rather than a placeholder when the payload has no name, which is the
          state every fixture and every pre-existing response is in. */}
      {hostName && (
        <p className="flex min-w-0 items-center gap-1.5 text-meta text-fg-muted">
          <span aria-hidden>{"\u{1F464}"}</span>
          <HostLink
            userId={event.hostId}
            name={hostName}
            avatarUrl={mediaUrl(event.hostPhotoUrl)}
            role={event.hostRole}
            size="sm"
            className="min-w-0 text-fg-muted hover:text-brand-600"
          />
          {isCreatorRole(event.hostRole) && (
            <Badge tone="info" className="shrink-0">
              {"\u{2B50} Creator"}
            </Badge>
          )}
        </p>
      )}

      <div
        className={cn(
          // mt-auto pins the count + actions to the bottom of the details
          // column, so they sit bottom-left regardless of how tall the poster
          // makes the card.
          "mt-auto flex flex-wrap items-center gap-2",
          // Compact already shows the same numbers under the title, so the
          // footer carries only the hero count and the CTA - repeating the
          // tally twice in a 90px card is the definition of clutter.
          compact ? "pt-1.5" : "pt-4",
          // Bleeds the tint to the card edges. The offsets have to match the
          // container's own padding, which is p-4 by default and p-3 in compact -
          // a single hardcoded -mx-4 would leave the strip visibly inset on one
          // of the two.
          kind === "joined" &&
            (compact
              ? "-mx-3 -mb-3 rounded-b-md border-t border-border bg-brand-tint px-3 py-2"
              : "-mx-4 -mb-4 rounded-b-md border-t border-border bg-brand-tint px-4 py-3"),
        )}
      >
        {!compact && <Count event={event} kind={kind} availability={availability} />}
        {!compact && <Tally event={event} />}
        {kind === "one-spot" && <Badge tone="warn">1 spot left</Badge>}
        {kind === "full" && <Badge tone="danger">Full</Badge>}
        {kind === "mine" && <Badge tone="brand">You&apos;re hosting</Badge>}
        {kind === "pending-review" && <Badge tone="warn">Pending Approval</Badge>}
        {/* Approval to *join*, which is a different rule from the pending-review
            badge above (approval to *publish*) and can hold at the same time. Its
            own tone and wording keep the two from reading as one state. */}
        {event.requiresJoinApproval === true && <Badge tone="info">Approval needed</Badge>}
        {kind === "cancelled" && <Badge tone="info">Cancelled</Badge>}
        {kind === "ended" && <Badge tone="neutral">Ended</Badge>}
        {kind === "ended" && event.isCancelled && <Badge tone="info">Cancelled</Badge>}
        {compact && event.skillLevel && (
          <Badge className="ml-auto" aria-label={`Skill level ${event.skillLevel}`}>
            {event.skillLevel}
          </Badge>
        )}
        {action && (
          <span
            className={cn("relative z-10", compact && !event.skillLevel && "ml-auto")}
            onClick={(e) => e.stopPropagation()}
            onKeyDown={(e) => e.stopPropagation()}
          >
            {action}
          </span>
        )}
      </div>
      </div>
      {!hideImage && (
        <Poster
          src={posterSrc}
          icon={icon}
          failed={thumbFailed}
          variant={compact ? "thumb" : "poster"}
          onError={() => setThumbFailed(true)}
        />
      )}
    </article>
  );
}

/**
 * The poster panel â€” the card's dominant visual. On mobile a full-width banner
 * above the details; at >=768px a fixed 280px column (roughly a third of the
 * card) that fills the card's full
 * height on the right. The compact card reuses this for its square thumbnail.
 * `object-cover` crops any source ratio (portrait,
 * landscape, square) to fill without stretching, and the group-hover scale(1.03)
 * is clipped by the card's `overflow-hidden`. With no image (or a failed load)
 * it keeps the exact same footprint and shows a placeholder, so the layout never
 * shifts between the two states.
 */
function Poster({
  src,
  icon,
  failed,
  variant = "poster",
  onError,
}: {
  src: string | null;
  icon: string | null;
  failed: boolean;
  /** `poster` is the dominant panel; `thumb` is the compact card's square leader. */
  variant?: "poster" | "thumb";
  onError: () => void;
}) {
  const thumb = variant === "thumb";
  return (
    <div
      aria-hidden
      className={cn(
        "relative shrink-0 overflow-hidden bg-surface-2",
        // The thumbnail leads the row (leftmost) and is vertically centred against
        // the text block, so it stays optically level with the title instead of
        // hanging off the top of a taller card.
        //
        // Square by construction rather than a matched pair of h-/w- classes:
        // aspect-square ties height to width, so the mobile size cannot silently
        // desynchronise from the desktop one the way `h-20 w-20 lg:h-32 lg:w-32`
        // invites. `object-cover` on the img below crops any source ratio into it.
        //
        // shrink-0 is load-bearing - the text column is min-w-0 and flex-1, so
        // without it the thumbnail absorbs the squeeze and stops being square.
        thumb
          ? "order-first aspect-square w-20 shrink-0 self-center rounded-md mr-3 lg:w-32"
          : "order-first h-48 w-full md:order-none md:h-auto md:w-70",
      )}
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
            <span
              className={cn("opacity-40", thumb ? "text-2xl lg:text-h1" : "text-h1")}
              aria-hidden
            >
              {"\u{1F5BC}"}
            </span>
          ) : (
            <>
              <span className="absolute inset-0 bg-[radial-gradient(circle_at_30%_20%,var(--brand),transparent_60%)] opacity-15" />
              {icon && (
                <span
                  className={cn("relative opacity-60", thumb ? "text-3xl lg:text-6xl" : "text-4xl")}
                  aria-hidden
                >
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
  availability: Availability;
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
        {formatSpotsRatio(event.joinedCount, event.maxParticipants)}
      </span>
      <span className="sr-only"> â€” {spotsTakenSentence(event.joinedCount, event.maxParticipants)}</span>
    </span>
  );
}

/**
 * The compact card's social-proof row, sitting directly under the title.
 *
 * Interest is shown beside the joined count and never added to it: an
 * "interested" tap reserves no spot, so folding the two together would overstate
 * how full an event is. The joined number keeps `data-count` so it animates on the
 * live optimistic update exactly as the footer hero count does.
 *
 * A zero interested count is still rendered rather than dropped. The row exists so
 * two events can be compared at a glance, and a metric that disappears at zero
 * makes "nobody is interested" indistinguishable from "this is not tracked".
 */
function SocialProof({
  event,
  availability,
}: {
  event: EventListItem;
  availability: Availability;
}) {
  return (
    <p className="flex flex-wrap items-center gap-x-2.5 gap-y-0.5 pt-0.5 text-micro text-fg-muted">
      <span className="inline-flex items-center gap-1">
        <span aria-hidden>{"\u{1F465}"}</span>
        <span
          data-count
          className={cn("font-semibold tabular-nums", countTextClass[availability])}
        >
          {formatSpotsRatio(event.joinedCount, event.maxParticipants)}
        </span>
        <span>joined</span>
        <span className="sr-only">
          {" "}
          - {spotsTakenSentence(event.joinedCount, event.maxParticipants)}
        </span>
      </span>
      <span className="inline-flex items-center gap-1">
        <span aria-hidden>{"\u2B50"}</span>
        <span className="font-semibold tabular-nums">{event.interestedCount}</span>
        <span>interested</span>
      </span>
    </p>
  );
}

/**
 * The two count lines the browse card shows (spec): joined against capacity, and
 * interested as a standalone tally. Interest reserves no spot, so it is shown
 * beside â€” never summed into â€” the joined number.
 */
function Tally({ event }: { event: EventListItem }) {
  return (
    <span className="flex flex-col text-meta text-fg-muted leading-tight">
      <span>
        {"\u{1F465}"} {formatSpotsRatio(event.joinedCount, event.maxParticipants)} joined
      </span>
      <span>
        {"\u2B50"} {event.interestedCount} interested
      </span>
    </span>
  );
}
