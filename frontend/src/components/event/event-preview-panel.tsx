"use client";

import { useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { HostLink } from "@/components/profile/host-link";
import { mediaUrl } from "@/lib/api";
import { cn } from "@/lib/cn";
import {
  formatCardWhen,
  formatFullDate,
  formatSpotsRatio,
  isFull as isEventFull,
  truncateDescription,
} from "@/lib/format";
import type { EventListItem } from "@/types/events";
import { isPendingReviewStatus } from "@/types/events";

export interface EventPreviewPanelProps {
  /** The selected event, or null when nothing is selected. The caller passes the
   *  row it already has from the browse list - no second fetch, so the panel is
   *  populated the instant the card is tapped. */
  event: EventListItem | null;
  onOpen: (id: string) => void;
  onClose: () => void;
  className?: string;
}

/**
 * The browse preview: what an event is, where it is, and who runs it, without
 * leaving the list. It mirrors the card highlighted beside it.
 *
 * Deliberately a plain aside rather than a dialog. It sits in the layout beside
 * the map, it must not trap focus, and it must not stop the user from clicking
 * another card - which is the whole reason it exists. The close button is a real
 * button and the panel never takes over the page.
 *
 * On narrow viewports the caller wraps it in a bottom-sheet Dialog instead, which
 * is where Escape-to-close comes from; the desktop panel is inline and has no
 * such affordance, because clicking another card already replaces it.
 *
 * The panel is mounted with `key={event.id}` by every caller. That is what keeps
 * it honest: a fresh panel per selection replays the entry animation and clears
 * the poster's failed state, so a poster that 404'd for one event cannot stay
 * hidden for the next.
 */
export function EventPreviewPanel({
  event,
  onOpen,
  onClose,
  className,
}: EventPreviewPanelProps) {
  const [posterFailed, setPosterFailed] = useState(false);

  if (!event) return null;

  const posterSrc = posterFailed ? null : mediaUrl(event.thumbnailUrl);
  const preview = truncateDescription(event.description);
  // The host travels with the feed row, so the preview names them without a
  // second request. Trimmed because a whitespace-only name from the DB would
  // otherwise render an avatar of nothing.
  const hostName = event.hostName?.trim() || null;
  const isPending = isPendingReviewStatus(event.status);
  const isFull = isEventFull(event.joinedCount, event.maxParticipants);

  return (
    <aside
      aria-label={`Preview of ${event.title}`}
      className={cn(
        "preview-panel flex min-h-0 flex-col overflow-hidden rounded-md border border-border bg-surface shadow-raise",
        className,
      )}
    >
      {/* Poster, when there is one. Fixed height so a tall upload cannot push the
          details below the fold; object-cover crops rather than letterboxes. */}
      {posterSrc && (
        <div className="relative h-36 w-full shrink-0 overflow-hidden bg-surface-2">
          {/* Plain <img>, not next/image: the bytes come from the API origin, which
              next/image would have to be told about - same call as the card and the
              detail hero. alt="" because the title below names the event. */}
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img
            src={posterSrc}
            alt=""
            onError={() => setPosterFailed(true)}
            className="h-full w-full object-cover"
          />
        </div>
      )}

      <div className="flex min-h-0 flex-1 flex-col gap-3 overflow-y-auto p-4">
        <div className="flex items-start gap-2">
          <h3 className="min-w-0 flex-1 text-h3 text-fg">{event.title}</h3>
          <button
            type="button"
            onClick={onClose}
            aria-label="Close preview"
            className="press -mr-1 -mt-1 grid h-8 w-8 shrink-0 place-items-center rounded-md text-fg-muted hover:bg-surface-2 hover:text-fg"
          >
            <span aria-hidden>{"\u2715"}</span>
          </button>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          {isPending && <Badge tone="warn">Pending Approval</Badge>}
          {event.isCancelled && <Badge tone="info">Cancelled</Badge>}
          {event.status === "Completed" && <Badge tone="neutral">Ended</Badge>}
          {isFull && !event.isCancelled && !isPending && <Badge tone="danger">Full</Badge>}
          {event.visibility === "Private" && <Badge tone="neutral">Private</Badge>}
          {event.skillLevel && <Badge>{event.skillLevel}</Badge>}
        </div>

        <dl className="flex flex-col gap-2 text-meta">
          <Row label="When" icon={"\u{1F4C5}"}>
            <span className="text-fg">{formatFullDate(event.startAt)}</span>
            <span className="text-fg-muted">
              {" · "}
              {formatCardWhen(event)}
            </span>
          </Row>
          <Row label="Where" icon={"\u{1F4CD}"}>
            <span className="text-fg">{event.venueName}</span>
            {event.address && <span className="block text-fg-muted">{event.address}</span>}
          </Row>
          <Row label="Host" icon={"\u{1F464}"}>
            {hostName ? (
              // The host links to their public profile. hostId is optional on the
              // wire, and HostLink falls back to a plain name+avatar pair when it is
              // absent, so this keeps the current rendering for older responses
              // rather than linking to /profile/undefined.
              <HostLink
                userId={event.hostId}
                name={hostName}
                avatarUrl={mediaUrl(event.hostPhotoUrl)}
                role={event.hostRole}
                size="sm"
              />
            ) : (
              <span className="text-fg-muted">Not stated</span>
            )}
          </Row>
          <Row label="Taking part" icon={"\u{1F465}"}>
            <span className="text-fg">
              {formatSpotsRatio(event.joinedCount, event.maxParticipants)} joined
            </span>
            <span className="text-fg-muted">
              {" · "}
              {"⭐"} {event.interestedCount} interested
            </span>
          </Row>
        </dl>

        {preview && (
          <p className="border-t border-border pt-3 text-meta leading-relaxed text-fg-muted">
            {preview}
          </p>
        )}

        <div className="mt-auto pt-1">
          <Button className="w-full" onClick={() => onOpen(event.id)}>
            View Full Event
          </Button>
        </div>
      </div>
    </aside>
  );
}

/** One label/value line. `dt` is screen-reader only - the icon and its word
 *  already carry the label visually, and repeating it reads as noise. */
function Row({
  label,
  icon,
  children,
}: {
  label: string;
  icon: string;
  children: React.ReactNode;
}) {
  return (
    <div className="flex gap-2">
      <span aria-hidden className="w-5 shrink-0 text-center">
        {icon}
      </span>
      <div className="min-w-0 flex-1">
        <dt className="sr-only">{label}</dt>
        <dd className="m-0">{children}</dd>
      </div>
    </div>
  );
}

