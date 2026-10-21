"use client";

import Link from "next/link";
import { useParams, usePathname, useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { JoinButton, deriveJoinState, type JoinState } from "@/components/event/join-button";
import { JoinRequestsPanel } from "@/components/event/join-requests-panel";
import { EventMap } from "@/components/map/event-map";
import { ErrorState } from "@/components/state/empty-error";
import { Avatar, AvatarStack } from "@/components/ui/avatar";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { FireButton } from "@/components/ui/fire-button";
import { MessageButton } from "@/components/messages/message-button";
import { ConfirmDialog } from "@/components/ui/dialog";
import { Input } from "@/components/ui/field";
import { SkeletonBlock } from "@/components/ui/skeleton";
import { toast } from "@/components/ui/toast";
import { useAuth } from "@/lib/auth/auth-provider";
import { signInReturnTo } from "@/lib/auth/types";
import { useNow } from "@/lib/use-now";
import { StickyFooter } from "@/components/layout/site-header";
import { useToggleInterest } from "@/features/events/use-interests";
import { useEventDetail, useJoinEvent } from "@/features/events/use-events";
import { sessionDay, sessionTime, useEventSessions } from "@/features/events/use-sessions";
import { deleteCancelledEvent, refreshEventCaches, setEventStatus } from "@/features/events/host-actions";
import { cn } from "@/lib/cn";
import { availabilityOf, countTextClass } from "@/lib/cn";
import { mediaUrl } from "@/lib/api";
import {
  formatCost,
  formatDistance,
  formatFullDate,
  formatTimeRange,
  isFull as isEventFull,
  spotsLeft,
  capacityLimit,
} from "@/lib/format";
import type { EventDetail } from "@/types/events";
import { isPendingReviewStatus } from "@/types/events";

/**
 * `/events/[id]` (spec §8), poster-first: a full-bleed hero image opens the page
 * so the event reads like a real listing rather than a form. Beneath the hero a
 * title strip carries the chips + primary Join, then a 70/30 two-column grid:
 * the story (venue, description, who's going, invite link) on the left and a
 * sticky action card (thumbnail, date/time/venue, spots, Interested + Join) on
 * the right. On mobile the columns collapse to the spec order - poster, title,
 * details, description, full-width Join (pinned footer), participants.
 *
 * Host `Edit` / `Cancel event` live in the `...` menu beside the title, with the
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
  // Which host confirmation is open. Cancel and delete both ask first; reopen
  // does not, because it is the undo.
  const [confirm, setConfirm] = useState<"cancel" | "delete" | null>(null);

  async function runHostAction(action: "cancel" | "reopen" | "delete") {
    setConfirm(null);
    try {
      if (action === "delete") {
        await deleteCancelledEvent(event.id);
        toast("Event deleted", "success");
        router.push("/my-events");
        void refreshEventCaches();
        return;
      }
      await setEventStatus(event.id, action);
      await refreshEventCaches();
      toast(action === "cancel" ? "Event cancelled" : "Event reopened", "success");
    } catch (error) {
      toast(error instanceof Error ? error.message : "Could not update this event", "info");
    }
  }

  useEffect(() => {
    const limit = capacityLimit(event.maxParticipants);
    document.title = `${event.title} (${limit === null ? `${event.joinedCount} going` : `${event.joinedCount}/${limit}`}) · MonaHub`;
    return () => {
      document.title = "MonaHub";
    };
  }, [event.title, event.joinedCount, event.maxParticipants]);

  const secondsUntilStart = Math.floor(
    (new Date(event.startAt).getTime() - now) / 1000,
  );
  // `isFull` from lib/format rather than a raw comparison: maxParticipants is
  // nullable, and `joinedCount >= null` coerces to `>= 0`, which would report
  // every unlimited event as full.
  const isFull = isEventFull(event.joinedCount, capacityLimit(event.maxParticipants));
  const requiresApproval = event.requiresJoinApproval === true;
  const isRequestPending = event.myJoinRequestStatus === "Pending";
  const derived = deriveJoinState({
    isHost: event.isHost,
    isCancelled: event.isCancelled,
    isJoined: event.isJoined,
    isFull,
    secondsUntilStart,
    isRequestPending,
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
    if (!result.ok) return;
    // A gated event took a request, not a spot. Saying "You're in" there would be
    // a lie the member discovers when the button still reads Request submitted, so
    // the copy follows the server's flag rather than the success of the call.
    toast(
      requiresApproval ? "Join request submitted" : "Joined",
      "success",
    );
  }
  async function handleLeave() {
    const result = await join.leave();
    if (result.ok) toast("You've left this event", "success");
  }

  const joinButton = (fullWidth: boolean) => (
    <JoinButton
      state={state}
      current={event.joinedCount}
      max={capacityLimit(event.maxParticipants)}
      isHost={event.isHost}
      isCancelled={event.isCancelled}
      failure={join.failure}
      size="lg"
      fullWidth={fullWidth}
      requiresApproval={requiresApproval}
      onJoin={() => void handleJoin()}
      onLeave={event.isJoined ? () => void handleLeave() : undefined}
    />
  );

  return (
    <>
      {/* A host landing here straight after submitting a public event sees the page
          they just wrote, and nothing on it says the event is not live yet. The
          detail endpoint is open to the host precisely so they can check their own
          unpublished event, so the state has to be stated here as well as in the
          toast that is already on its way out. Only the host sees it: to everyone
          else the event is simply not joinable, which the JoinButton already says. */}
      {event.isHost && isPendingReviewStatus(event.status) && (
        <div
          role="status"
          className="border-b border-warn/40 bg-warn-tint px-4 py-3 text-center text-meta text-fg"
        >
          Pending approval — this event is hidden from Browse until an administrator
          approves it.
        </div>
      )}

      {/* Full-bleed hero: the poster is the first thing on the page, edge to
          edge, so context lands before a single word is read. */}
      <HeroPoster event={event} />

      <div className="mx-auto w-full max-w-[1120px] px-4 pb-6">
        <DetailHeader
          event={event}
          menu={
            event.isHost ? (
              <HostMenu
                eventId={event.id}
                isCancelled={event.isCancelled}
                onCancel={() => setConfirm("cancel")}
                onReopen={() => void runHostAction("reopen")}
                onDelete={() => setConfirm("delete")}
              />
            ) : null
          }
          joinSlot={joinButton(false)}
        />

        <div className="mt-6 grid grid-cols-1 gap-6 lg:grid-cols-[minmax(0,7fr)_minmax(0,3fr)] lg:items-start">
          {/* LEFT (70%): the story. Order matches the mobile spec - details,
              description, then who's going (participants sit last on mobile). */}
          <div className="flex min-w-0 flex-col gap-6">
            <section aria-labelledby="venue-heading">
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

            {event.description && (
              <section aria-labelledby="desc-heading">
                <h2 id="desc-heading" className="text-h3 text-fg">
                  About this event
                </h2>
                <p className="mt-2 whitespace-pre-line text-body leading-relaxed text-fg-muted">
                  {event.description}
                </p>
              </section>
            )}

            {/* Only a private event needs this: it is absent from Browse, so the link is
                the sole way anyone else reaches it. Mounted after the event resolves, so
                window.location is defined. */}
            {event.visibility === "Private" && <InviteLink eventId={event.id} />}

            {/* The host's moderation queue, and only where it can have entries:
                their own event, with approval turned on. Rendering it for an open
                event would offer a panel that can never hold anything. */}
            {event.isHost && event.requiresJoinApproval === true && (
              <section aria-labelledby="join-requests-heading">
                <span id="join-requests-heading" className="sr-only">
                  Join requests
                </span>
                <JoinRequestsPanel eventId={event.id} />
              </section>
            )}

            <section aria-labelledby="who-heading">
              <h2 id="who-heading" className="text-h3 text-fg">
                Who&apos;s going
              </h2>
              <ParticipantList participants={event.participants} />
            </section>
          </div>

          {/* RIGHT (30%): sticky action card. Hidden on mobile, whose CTA lives
              in the pinned footer below. */}
          <aside className="hidden lg:block">
            <div className="sticky top-20">
              <ActionCard event={event} joinSlot={joinButton(false)} />
            </div>
          </aside>
        </div>
      </div>

      {/* Mobile keeps the CTA pinned to the footer bar with the safe-area inset
          (spec §6); desktop gets it in the title strip and the sticky card. */}
      <div className="lg:hidden">
        <StickyFooter>{joinButton(true)}</StickyFooter>
      </div>

      <ConfirmDialog
        open={confirm === "cancel"}
        titleId="cancel-event-title"
        title={`Cancel "${event.title}"?`}
        body={`${event.joinedCount} ${event.joinedCount === 1 ? "person has" : "people have"} joined.`}
        confirmLabel="Cancel event"
        onClose={() => setConfirm(null)}
        onConfirm={() => void runHostAction("cancel")}
      />
      <ConfirmDialog
        open={confirm === "delete"}
        titleId="delete-event-title"
        title={`Delete "${event.title}"?`}
        body="It will be removed from your events and its link will stop working. This can't be undone."
        confirmLabel="Delete event"
        onClose={() => setConfirm(null)}
        onConfirm={() => void runHostAction("delete")}
      />
    </>
  );
}

/**
 * The sticky right-column card: poster thumbnail, the when/where essentials, a
 * prominent spots-remaining count, and the Interested + Join actions. It repeats
 * the hero's poster as a small anchor so the identity stays with the CTA as the
 * user scrolls the left column.
 */
function ActionCard({ event, joinSlot }: { event: EventDetail; joinSlot: React.ReactNode }) {
  const limit = capacityLimit(event.maxParticipants);
  const left = spotsLeft(event.joinedCount, limit);
  const availability = availabilityOf(event.joinedCount, limit);
  return (
    <div className="overflow-hidden rounded-lg border border-border bg-surface shadow-raise">
      <Thumb event={event} />
      <div className="flex flex-col gap-3 p-4">
        <div className="flex flex-col gap-2">
          <Row icon={"\u{1F4C5}"} label="Date" value={formatFullDate(event.startAt)} />
          <Row
            icon={"\u{1F552}"}
            label="Time"
            value={formatTimeRange(event.startAt, event.endAt)}
          />
          {/* One session of a series: joining here takes a spot in this session
              only, which the label says so nobody expects the whole term. */}
          {event.isRecurring && (
            <Row
              icon={"\u{1F502}"}
              label="Repeats (each session joined separately)"
              value={event.recurrenceSummary ?? "Repeating event"}
            />
          )}
          <Row icon={"\u{1F4CD}"} label="Venue" value={event.venueName} />
          <Row
            icon={"\u{1F4B0}"}
            label="Cost"
            value={event.cost ? `${formatCost(event.cost)} / person` : "Free"}
          />
        </div>

        {event.isRecurring && <SessionPicker event={event} />}

        <div className="rounded-md bg-surface-2 px-3 py-2">
          <span className="text-micro uppercase text-fg-muted">Spots remaining</span>
          <p className={cn("text-count", countTextClass[availability])} data-count>
            {limit === null ? "Open to all" : left === 0 ? "Full" : `${left} of ${limit}`}
          </p>
        </div>

        <InterestButton event={event} />
        {joinSlot}
        <MessageButton
          userId={event.hostId}
          label="Message host"
          draft={`Hi! About ${event.title}: `}
          className="h-11 w-full justify-center text-meta"
        />
      </div>
    </div>
  );
}

/** The sticky card's poster thumbnail - same placeholder discipline as the hero. */
function Thumb({ event }: { event: EventDetail }) {
  const [failed, setFailed] = useState(false);
  const src = failed ? null : mediaUrl(event.thumbnailUrl);
  return (
    <div className="relative h-36 w-full overflow-hidden border-b border-border bg-surface-2">
      {src ? (
        // eslint-disable-next-line @next/next/no-img-element
        <img
          src={src}
          alt=""
          onError={() => setFailed(true)}
          className="absolute inset-0 h-full w-full object-cover"
        />
      ) : (
        <>
          <div
            aria-hidden
            className="absolute inset-0 bg-[radial-gradient(circle_at_30%_20%,var(--brand),transparent_60%)] opacity-20"
          />
          {event.sportIcon && (
            <span
              className="absolute inset-0 flex items-center justify-center text-3xl opacity-60"
              aria-hidden
            >
              {event.sportIcon}
            </span>
          )}
        </>
      )}
    </div>
  );
}

/** A labelled when/where line inside the sticky card. */
function Row({ icon, label, value }: { icon: string; label: string; value: string }) {
  return (
    <div className="flex items-start gap-2">
      <span aria-hidden className="mt-px text-meta">
        {icon}
      </span>
      <div className="flex min-w-0 flex-col leading-tight">
        <span className="text-micro uppercase text-fg-muted">{label}</span>
        <span className="text-body text-fg">{value}</span>
      </div>
    </div>
  );
}

/**
 * "Select session date" for a session of a recurring series. Every session is its
 * own event with its own spots, so picking a date opens that session's page, and
 * Join / Interested on it apply to that date only. The current session is selected;
 * dates you are going to say so.
 */
function SessionPicker({ event }: { event: EventDetail }) {
  const router = useRouter();
  const { sessions } = useEventSessions(event.id);
  const id = `session-picker-${event.id}`;
  if (!sessions || sessions.length < 2) return null;

  return (
    <div className="flex flex-col gap-1">
      <label htmlFor={id} className="text-micro uppercase text-fg-muted">
        Select session date
      </label>
      <select
        id={id}
        value={event.id}
        onChange={(e) => router.push(`/events/${e.target.value}`)}
        className="w-full rounded-md border border-border bg-surface px-3 py-2 text-body text-fg focus:border-brand-600 focus:outline-none"
      >
        {sessions.map((s) => {
          const full = s.maxParticipants !== null && s.joinedCount >= s.maxParticipants;
          const note = s.isCancelled ? " (cancelled)" : s.isJoined ? " (going)" : full ? " (full)" : "";
          return (
            <option key={s.id} value={s.id}>
              {sessionDay(s.startAt, event.timezone)}, {sessionTime(s.startAt, event.timezone)}
              {note}
            </option>
          );
        })}
      </select>
      <p className="text-micro text-fg-muted">Joining and Interested apply to the selected date.</p>
    </div>
  );
}

/**
 * The Interested toggle for the sticky card. It lives here rather than in the
 * shared `CardActions` because that component renders Interested *and* Join
 * together, and the redesign splits them: Join is the `JoinButton` state machine
 * (with rollback copy), Interested is this standalone toggle.
 */
function InterestButton({ event }: { event: EventDetail }) {
  const { user } = useAuth();
  const router = useRouter();
  const pathname = usePathname();
  const { toggle, isPending } = useToggleInterest(event.id);

  async function onClick() {
    if (!user) {
      router.push(signInReturnTo(pathname));
      return;
    }
    const result = await toggle();
    if (result.ok)
      toast(result.isInterested ? "Marked interested" : "Removed from interested", "success");
  }

  return (
    <FireButton
      label="Interested"
      active={event.isInterested}
      size="md"
      fullWidth
      loading={isPending}
      disabled={event.isJoined || event.isCancelled}
      onClick={() => void onClick()}
    />
  );
}

function DetailSkeleton() {
  return (
    <div className="mx-auto w-full max-w-[1120px] px-4 py-4" role="status" aria-label="Loading event">
      <SkeletonBlock className="h-[350px] w-full rounded-lg sm:h-[420px]" />
      <SkeletonBlock className="mt-5 h-4 w-24" />
      <SkeletonBlock className="mt-3 h-9 w-3/4" />
      <div className="mt-3 flex gap-2">
        <SkeletonBlock className="h-6 w-24 rounded-full" />
        <SkeletonBlock className="h-6 w-24 rounded-full" />
        <SkeletonBlock className="h-6 w-20 rounded-full" />
      </div>
      <div className="mt-6 grid grid-cols-1 gap-6 lg:grid-cols-[minmax(0,7fr)_minmax(0,3fr)]">
        <div className="flex flex-col gap-4">
          <SkeletonBlock className="h-5 w-20" />
          <SkeletonBlock className="h-[200px] w-full" />
          <SkeletonBlock className="h-5 w-32" />
          <SkeletonBlock className="h-24 w-full" />
        </div>
        <SkeletonBlock className="h-72 w-full rounded-lg" />
      </div>
    </div>
  );
}


/**
 * The full-width hero poster. `object-cover` crops any source ratio to fill the
 * 350-500px band without stretching; a bottom gradient keeps the overlaid
 * title/meta legible on any image. With no image (or a failed load) it keeps the
 * exact same box and shows a brand-tint placeholder, so nothing shifts.
 */
function HeroPoster({ event }: { event: EventDetail }) {
  const [failed, setFailed] = useState(false);
  const src = failed ? null : mediaUrl(event.thumbnailUrl);
  return (
    <section
      aria-label={`${event.title} poster`}
      className="mx-auto mt-4 w-full max-w-[1120px] px-4"
    >
      <div className="relative h-[350px] w-full overflow-hidden rounded-lg border border-border bg-surface-2 shadow-raise sm:h-[420px] lg:h-[460px]">
        {src ? (
          // Plain <img>, not next/image: the bytes come from the API origin,
          // which isn't a configured image domain, and these are the host's own
          // files. alt="" - the overlaid title names the event; the image is
          // decoration.
          // eslint-disable-next-line @next/next/no-img-element
          <img
            src={src}
            alt=""
            onError={() => setFailed(true)}
            className="absolute inset-0 h-full w-full object-cover"
          />
        ) : (
          <div
            aria-hidden
            className="absolute inset-0 bg-[radial-gradient(circle_at_25%_15%,var(--brand),transparent_60%)]"
          />
        )}
        {/* Dark scrim: guarantees contrast for the overlaid text regardless of
            the poster's brightness. */}
        <div
          aria-hidden
          className="absolute inset-0 bg-gradient-to-t from-black/80 via-black/25 to-transparent"
        />
        <div className="absolute inset-x-0 bottom-0 p-5 sm:p-8">
          <div className="flex flex-wrap items-center gap-2">
            {event.skillLevel && (
              <Badge tone="brand" className="bg-white/90 text-black">
                {event.skillLevel}
              </Badge>
            )}
            {event.isCancelled && (
              <Badge tone="info" className="bg-white/90 text-black">
                Cancelled
              </Badge>
            )}
          </div>
          <h1 className="mt-3 max-w-3xl text-hero text-white drop-shadow sm:text-[2.5rem] sm:leading-[1.1]">
            {event.title}
          </h1>
          <p className="mt-2 flex flex-wrap items-center gap-x-3 gap-y-1 text-meta font-medium text-white/90">
            <span>{formatFullDate(event.startAt)}</span>
            <span aria-hidden>{"\u00B7"}</span>
            <span>{formatTimeRange(event.startAt, event.endAt)}</span>
            <span aria-hidden>{"\u00B7"}</span>
            <span>{event.venueName}</span>
          </p>
        </div>
      </div>
    </section>
  );
}

/**
 * Title strip under the hero: back link, the authoritative `<h1>` + host menu,
 * the metadata chips (skill, cost, spots, distance, tags, visibility), and the
 * desktop primary Join. The hero carries a decorative duplicate of the title;
 * this is the one that names the page.
 */
function DetailHeader({
  event,
  menu,
  joinSlot,
}: {
  event: EventDetail;
  menu: React.ReactNode;
  joinSlot: React.ReactNode;
}) {
  const limit = capacityLimit(event.maxParticipants);
  const left = spotsLeft(event.joinedCount, limit);
  const availability = availabilityOf(event.joinedCount, limit);
  return (
    <header>
      <Link
        href="/"
        className="press mt-5 inline-flex items-center gap-1 text-meta text-fg-muted no-underline hover:text-fg"
      >
        <span aria-hidden>{"\u2190"}</span> All events
      </Link>

      <div className="mt-3 flex items-start gap-2">
        <h1 className="text-hero text-fg">{event.title}</h1>
        {menu}
      </div>

      <div className="mt-3 flex flex-wrap items-center gap-2">
        {event.skillLevel && <Badge>{event.skillLevel}</Badge>}
        <Badge tone={event.cost ? "neutral" : "brand"}>
          {event.cost ? `${formatCost(event.cost)} / person` : "Free"}
        </Badge>
        <Badge
          tone={
            availability === "full"
              ? "danger"
              : availability === "one-spot"
                ? "warn"
                : "neutral"
          }
        >
          {left === null
            ? "Open event"
            : left === 0
              ? "Full"
              : `${left} ${left === 1 ? "spot" : "spots"} left`}
        </Badge>
        {event.distanceKm !== null && <Badge>{formatDistance(event.distanceKm)}</Badge>}
        {/* One badge per tag; hidden when the event has none instead of showing
            an empty pill. */}
        {event.tags.map((tag) => (
          <Badge key={tag} tone="brand">
            #{tag}
          </Badge>
        ))}
        {/* Discovery flag, not a lock: the event is open to anyone holding the link. */}
        {event.visibility === "Private" && <Badge tone="warn">Private {"\u00B7"} link only</Badge>}
        {event.isCancelled && <Badge tone="info">Cancelled</Badge>}
      </div>

      <div className="mt-5 hidden max-w-sm lg:block">{joinSlot}</div>
    </header>
  );
}

/** `...` menu, destructive action last (spec §8). A live event offers Edit and
 *  Cancel; a cancelled one offers Reopen and Delete - deleting is only possible once
 *  the event is cancelled, the step that tells the people who joined. */
function HostMenu({
  eventId,
  isCancelled,
  onCancel,
  onReopen,
  onDelete,
}: {
  eventId: string;
  isCancelled: boolean;
  onCancel: () => void;
  onReopen: () => void;
  onDelete: () => void;
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
          {isCancelled ? (
            <button
              type="button"
              role="menuitem"
              onClick={() => {
                setOpen(false);
                onReopen();
              }}
              className="block w-full px-3 py-2 text-left text-meta text-fg hover:bg-surface-2"
            >
              Reopen event
            </button>
          ) : (
            <Link
              href={`/create?edit=${eventId}`}
              role="menuitem"
              onClick={() => setOpen(false)}
              className="block px-3 py-2 text-meta text-fg no-underline hover:bg-surface-2"
            >
              Edit
            </Link>
          )}
          <button
            type="button"
            role="menuitem"
            onClick={() => {
              setOpen(false);
              if (isCancelled) onDelete();
              else onCancel();
            }}
            className="block w-full px-3 py-2 text-left text-meta text-danger hover:bg-surface-2"
          >
            {isCancelled ? "Delete event" : "Cancel event"}
          </button>
        </div>
      )}
    </div>
  );
}

/**
 * The host-facing half of a private event: it is deliberately missing from
 * Browse, so this page is the only place its URL surfaces. The absolute link is
 * built at render time rather than in markup, since `window` does not exist
 * during the server pass.
 */
function InviteLink({ eventId }: { eventId: string }) {
  const [copied, setCopied] = useState(false);
  const url = typeof window === "undefined" ? "" : `${window.location.origin}/events/${eventId}`;

  async function copy() {
    try {
      await navigator.clipboard.writeText(url);
      setCopied(true);
      toast("Link copied - anyone with it can join", "success");
    } catch {
      // Clipboard access is denied outside a secure context or without permission;
      // the field below is already selected-ready, so say what to do instead.
      toast("Couldn't copy automatically - select the link and copy it");
    }
  }

  return (
    <section className="mt-6" aria-labelledby="invite-heading">
      <h2 id="invite-heading" className="text-h3 text-fg">
        Invite link
      </h2>
      <p className="mt-1 text-meta text-fg-muted">
        This event is private, so it does not appear on Browse. Send this link to the people you
        want - it opens for them and they can take a spot.
      </p>
      <div className="mt-3 flex flex-col gap-2 sm:flex-row">
        <Input
          readOnly
          value={url}
          aria-label="Private event link"
          onFocus={(event) => event.currentTarget.select()}
        />
        <Button type="button" variant="secondary" onClick={() => void copy()} className="sm:w-40">
          {copied ? "Copied" : "Copy link"}
        </Button>
      </div>
    </section>
  );
}

/**
 * Who's going: the joined count, an overlapping avatar stack, then the full
 * roster. An empty roster gets a bordered, centred placeholder rather than a
 * bare sentence, so the section never reads as broken or unfinished.
 */
function ParticipantList({ participants }: { participants: EventDetail["participants"] }) {
  if (participants.length === 0) {
    return (
      <div className="mt-2 flex flex-col items-center gap-1 rounded-md border border-dashed border-border px-4 py-6 text-center">
        <span className="text-2xl" aria-hidden>
          {"\u{1F464}"}
        </span>
        <p className="text-body text-fg">No one has joined yet</p>
        <p className="text-meta text-fg-muted">Be the first to take a spot.</p>
      </div>
    );
  }
  return (
    <>
      <div className="mt-2 flex items-center gap-3">
        <AvatarStack people={participants} size="md" />
        <span className="text-meta text-fg-muted">
          {participants.length} {participants.length === 1 ? "person" : "people"} going
        </span>
      </div>
      <ul className="mt-3 flex flex-col gap-2">
        {participants.map((p) => (
          <li key={p.id} className="flex items-center gap-3">
            <Avatar name={p.displayName} src={p.avatarUrl} size="sm" />
            <span className="text-meta text-fg">{p.displayName}</span>
          </li>
        ))}
      </ul>
    </>
  );
}
