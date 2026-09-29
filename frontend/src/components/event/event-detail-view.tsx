"use client";

import Link from "next/link";
import { useParams, usePathname, useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { JoinButton, deriveJoinState, type JoinState } from "@/components/event/join-button";
import { EventMap } from "@/components/map/event-map";
import { ErrorState } from "@/components/state/empty-error";
import { Avatar } from "@/components/ui/avatar";
import { Badge } from "@/components/ui/badge";
import { ConfirmDialog } from "@/components/ui/dialog";
import { SkeletonBlock } from "@/components/ui/skeleton";
import { toast } from "@/components/ui/toast";
import { useAuth } from "@/lib/auth/auth-provider";
import { signInReturnTo } from "@/lib/auth/types";
import { useNow } from "@/lib/use-now";
import { StickyFooter } from "@/components/layout/site-header";
import { CardActions } from "@/components/event/card-actions";
import { useEventDetail, useJoinEvent } from "@/features/events/use-events";
import {
  formatCost,
  formatDistance,
  formatFullDate,
  formatTimeRange,
  spotsTakenSentence,
} from "@/lib/format";
import type { EventDetail } from "@/types/events";

/**
 * `/events/[id]` (spec §8): title, sport+skill chips, `2 of 4 spots taken`, host
 * row with `Host` badge, description, full-width venue map (200px), address,
 * sticky footer CTA. Host `Edit` / `Cancel event` live in a `...` menu with the
 * consequence named in the confirm modal.
 */
export function EventDetailPage() {
  const params = useParams<{ id: string }>();
  const id = params.id;
  const { event, error, isLoading, mutate } = useEventDetail(id);

  if (isLoading && !event) return <DetailSkeleton />;
  if (error && !event) {
    return (
      <div className="mx-auto w-full max-w-[680px] p-4">
        <ErrorState
          message="This event could not be loaded - try again, or head back to Browse."
          detail={String(error)}
          onRetry={() => void mutate()}
        />
      </div>
    );
  }
  if (!event) {
    return (
      <div className="mx-auto w-full max-w-[680px] p-4">
        <ErrorState
          title="Event not found"
          message="It may have been cancelled or deleted."
        />
      </div>
    );
  }
  return <DetailBody event={event} />;
}


function DetailBody({ event }: { event: EventDetail }) {
  const join = useJoinEvent(event);
  const { user } = useAuth();
  const router = useRouter();
  const pathname = usePathname();
  const now = useNow();
  const [confirmCancel, setConfirmCancel] = useState(false);

  useEffect(() => {
    document.title = `${event.title} (${event.participantCount}/${event.maxParticipants}) - Event Manager`;
    return () => {
      document.title = "Event Manager";
    };
  }, [event.title, event.participantCount, event.maxParticipants]);

  const secondsUntilStart = Math.floor(
    (new Date(event.startAt).getTime() - now) / 1000,
  );
  const isFull = event.participantCount >= event.maxParticipants;
  const derived = deriveJoinState({
    isHost: event.isHost,
    isCancelled: event.isCancelled,
    isJoined: event.isJoined,
    isFull,
    secondsUntilStart,
  });
  const state: JoinState = join.isJoining || join.isLeaving
    ? "joining"
    : join.failure === "network" || join.failure === "server"
      ? "error"
      : derived;

  async function handleJoin() {
    // Sign-in first: joining is the one write a visitor is likely to attempt,
    // and sending them to `/login?next=` here means they come straight back to
    // the event they wanted rather than to the browse page.
    if (!user) {
      router.push(signInReturnTo(pathname));
      return;
    }
    const result = await join.join();
    if (result.ok) toast("You're in", "success");
  }
  async function handleLeave() {
    const result = await join.leave();
    if (result.ok) toast("You've left this event", "success");
  }

  const joinButton = (fullWidth: boolean) => (
    <JoinButton
      state={state}
      current={event.participantCount}
      max={event.maxParticipants}
      isHost={event.isHost}
      isCancelled={event.isCancelled}
      failure={join.failure}
      size="lg"
      fullWidth={fullWidth}
      onJoin={() => void handleJoin()}
      onLeave={event.isJoined ? () => void handleLeave() : undefined}
    />
  );

  return (
    <>
      <DetailHeader
        event={event}
        menu={
          event.isHost ? (
            <HostMenu eventId={event.id} onCancelRequest={() => setConfirmCancel(true)} />
          ) : null
        }
      />

      <section className="mt-6" aria-labelledby="host-heading">
        <h2 id="host-heading" className="text-h3 text-fg">
          Host
        </h2>
        <div className="mt-2 flex items-center gap-3">
          <Avatar name={event.host.displayName} src={event.host.avatarUrl} size="md" />
          <span className="text-body text-fg">{event.host.displayName}</span>
          <Badge tone="brand" className="ml-auto">
            Host
          </Badge>
        </div>
      </section>

      {event.description && (
        <section className="mt-6" aria-labelledby="desc-heading">
          <h2 id="desc-heading" className="text-h3 text-fg">
            Details
          </h2>
          <p className="mt-2 whitespace-pre-line text-body leading-relaxed text-fg-muted">
            {event.description}
          </p>
        </section>
      )}

      <section className="mt-6" aria-labelledby="interest-heading">
        <h2 id="interest-heading" className="text-h3 text-fg">
          Interested
        </h2>
        <p className="mt-1 text-meta text-fg-muted">
          Keep this event in your list, or take a spot now.
        </p>
        <div className="mt-3">
          <CardActions
            eventId={event.id}
            isHost={event.isHost}
            isCancelled={event.isCancelled}
            size="md"
          />
        </div>
      </section>

      <section className="mt-6" aria-labelledby="who-heading">
        <h2 id="who-heading" className="text-h3 text-fg">
          Who&apos;s going
        </h2>
        <ParticipantList participants={event.participants} />
      </section>

      <section className="mt-6" aria-labelledby="venue-heading">
        <h2 id="venue-heading" className="text-h3 text-fg">
          Venue
        </h2>
        <div className="mt-2 overflow-hidden rounded-md border border-border">
          <EventMap
            events={[event]}
            selectedEventId={event.id}
            onSelect={() => {}}
            variant="detail"
          />
        </div>
        <address className="mt-2 text-meta not-italic text-fg-muted">{event.address}</address>
      </section>

      {/* Desktop keeps the CTA inline in the flow; mobile pins it to the footer
          bar with the safe-area inset (spec §6). */}
      <div className="mt-8 hidden lg:block">{joinButton(false)}</div>
      <div className="lg:hidden">
        <StickyFooter>{joinButton(true)}</StickyFooter>
      </div>

      <ConfirmDialog
        open={confirmCancel}
        titleId="cancel-event-title"
        title={`Cancel "${event.title}"?`}
        body={`${event.participantCount} ${event.participantCount === 1 ? "person has" : "people have"} joined.`}
        confirmLabel="Cancel event"
        onClose={() => setConfirmCancel(false)}
        onConfirm={() => {
          setConfirmCancel(false);
          toast("Event cancelled", "success");
        }}
      />
    </>
  );
}
function Meta({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col">
      <dt className="text-micro uppercase text-fg-muted">{label}</dt>
      <dd className="text-body text-fg">{value}</dd>
    </div>
  );
}

function DetailSkeleton() {
  return (
    <div className="mx-auto w-full max-w-[680px] px-4 py-4" role="status" aria-label="Loading event">
      <SkeletonBlock className="h-4 w-24" />
      <SkeletonBlock className="mt-3 h-8 w-3/4" />
      <div className="mt-3 flex gap-2">
        <SkeletonBlock className="h-6 w-24 rounded-full" />
        <SkeletonBlock className="h-6 w-24 rounded-full" />
      </div>
      <SkeletonBlock className="mt-5 h-6 w-40" />
      <SkeletonBlock className="mt-4 h-16 w-full" />
      <SkeletonBlock className="mt-6 h-5 w-20" />
      <SkeletonBlock className="mt-3 h-9 w-56" />
      <SkeletonBlock className="mt-6 h-5 w-20" />
      <SkeletonBlock className="mt-3 h-[200px] w-full" />
    </div>
  );
}

/** Back link, title, tag/skill/cancelled chips and the meta grid (spec §8). */
function DetailHeader({
  event,
  menu,
}: {
  event: EventDetail;
  menu: React.ReactNode;
}) {
  return (
    <header>
      <Link
        href="/"
        className="press inline-flex items-center gap-1 text-meta text-fg-muted no-underline hover:text-fg"
      >
        <span aria-hidden>{"\u2190"}</span> All events
      </Link>

      <div className="mt-3 flex items-start gap-2">
        <h1 className="text-h1 text-fg">
          {event.sportIcon && <span aria-hidden>{event.sportIcon}</span>} {event.title}
        </h1>
        {menu}
      </div>

      <div className="mt-2 flex flex-wrap items-center gap-2">
        {/* One badge per tag rather than the old single sport badge; hidden when
            the event has none instead of showing an empty pill. */}
        {event.tags.map((tag) => (
          <Badge key={tag} tone="brand">
            #{tag}
          </Badge>
        ))}
        {event.skillLevel && <Badge>{event.skillLevel}</Badge>}
        {event.isCancelled && <Badge tone="info">Cancelled</Badge>}
      </div>

      <p className="mt-4 text-body text-fg">
        <span data-count className="font-semibold">
          {spotsTakenSentence(event.participantCount, event.maxParticipants)}
        </span>
      </p>

      <dl className="mt-4 grid grid-cols-1 gap-x-4 gap-y-3 text-meta sm:grid-cols-2">
        <Meta
          label="When"
          value={`${formatFullDate(event.startAt)} ${formatTimeRange(event.startAt, event.endAt)}`}
        />
        <Meta label="Where" value={event.venueName} />
        <Meta label="Cost" value={formatCost(event.cost)} />
        {event.distanceKm !== null && (
          <Meta label="Distance" value={formatDistance(event.distanceKm)} />
        )}
      </dl>
    </header>
  );
}

/** `...` menu, destructive action last (spec §8). */
function HostMenu({
  eventId,
  onCancelRequest,
}: {
  eventId: string;
  onCancelRequest: () => void;
}) {
  const [open, setOpen] = useState(false);
  return (
    <div className="relative ml-auto">
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        aria-haspopup="menu"
        aria-expanded={open}
        aria-label="Host actions"
        className="press h-9 w-9 rounded-md border border-border bg-surface text-fg"
      >
        {"\u22EF"}
      </button>
      {open && (
        <div
          role="menu"
          aria-label="Host actions"
          className="absolute right-0 z-10 mt-1 w-44 overflow-hidden rounded-md border border-border bg-surface shadow-float"
        >
          <Link
            href={`/create?edit=${eventId}`}
            role="menuitem"
            onClick={() => setOpen(false)}
            className="block px-3 py-2 text-meta text-fg no-underline hover:bg-surface-2"
          >
            Edit
          </Link>
          <button
            type="button"
            role="menuitem"
            onClick={() => {
              setOpen(false);
              onCancelRequest();
            }}
            className="block w-full px-3 py-2 text-left text-meta text-danger hover:bg-surface-2"
          >
            Cancel event
          </button>
        </div>
      )}
    </div>
  );
}

function ParticipantList({ participants }: { participants: EventDetail["participants"] }) {
  if (participants.length === 0) {
    return <p className="mt-2 text-meta text-fg-muted">Nobody has joined yet.</p>;
  }
  return (
    <ul className="mt-2 flex flex-col gap-2">
      {participants.map((p) => (
        <li key={p.id} className="flex items-center gap-3">
          <Avatar name={p.displayName} src={p.avatarUrl} size="sm" />
          <span className="text-meta text-fg">{p.displayName}</span>
        </li>
      ))}
    </ul>
  );
}
