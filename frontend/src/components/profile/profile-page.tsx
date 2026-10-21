"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { EventCard } from "@/components/event/event-card";
import { FollowButton } from "@/components/profile/follow-button";
import { MessageButton } from "@/components/messages/message-button";
import { ErrorState } from "@/components/state/empty-error";
import { CardError } from "@/components/account/card-error";
import { Avatar } from "@/components/ui/avatar";
import { Card } from "@/components/ui/card";
import { SkeletonBlock } from "@/components/ui/skeleton";
import { RoleBadge, isCreatorRole } from "@/components/admin/role-badge";
import {
  useProfileActivity,
  useProfileEvents,
  useProfileJoined,
  useProfileSummary,
  useProfileUpcoming,
} from "@/features/profile/use-profile";
import { mediaUrl } from "@/lib/api";
import { useAuth } from "@/lib/auth/auth-provider";
import { cn } from "@/lib/cn";
import type { EventListItem } from "@/types/events";
import type { ProfileActivity, ProfileStats, ProfileSummary } from "@/types/profile";

/**
 * The public profile: `/profile/[id]`.
 *
 * Its purpose is trust. A reader arrives from an event card asking "is this host
 * real and active?", and the page answers with volume and recency - how many events
 * they have run, how many are coming, whether people turn up - rather than with
 * anything about the viewer.
 *
 * Everything on it is public. That is a constraint on this file, not just on the API:
 * host-only numbers live behind `GET /api/events/{id}/analytics`, which 404s for
 * anyone but the host, so the components here have no way to obtain a view count even
 * if one were added to a section. The Activity feed names events the person created,
 * hosted, joined or was interested in - and only ever public ones, filtered in SQL.
 */

/** The Overview tiles. `joinedEvents` is the spec's fourth stat and the one the
 * account page does not have; `interestedEvents` is worded "Interested in" so it
 * reads as an activity rather than an achievement.
 *
 * Followers is deliberately absent from this grid. It is the header's number rather
 * than one of the host's activity stats — it measures the audience rather than the
 * host's output, it is the only one that changes when the reader taps something, and
 * the spec pairs it with the Follow control. Sitting beside the button is also the
 * only place where a count moving under your own click reads as caused by it. */
const TILES: { key: keyof ProfileStats; label: string }[] = [
  { key: "eventsCreated", label: "Events created" },
  { key: "upcomingEvents", label: "Upcoming events" },
  { key: "joinedEvents", label: "Joined events" },
  { key: "interestedEvents", label: "Interested in" },
];

export function ProfilePage() {
  const params = useParams<{ id: string }>();
  const id = params?.id ?? "";

  // The summary is the one profile read whose answer depends on who is asking, so it
  // is keyed on the signed-in user's id and refetches when the session resolves.
  // Nothing else on the page moves: the lists and the feed are the same for everyone.
  const { user } = useAuth();
  const { summary, error, isLoading, retry } = useProfileSummary(id, user?.id);
  const activity = useProfileActivity(id);
  const hosted = useProfileEvents(id);
  const upcoming = useProfileUpcoming(id);
  const joined = useProfileJoined(id);

  if (!id) {
    return <ErrorState message="This profile link is incomplete." />;
  }

  if (error) {
    // 404 and a network failure are the same screen from the reader's side: they
    // followed a link and got nothing. Retrying is offered because a 404 from a
    // profile is more often a stale id than a genuinely absent person, and the
    // distinction would only matter to whoever minted the link.
    return (
      <div className="mx-auto w-full max-w-3xl px-4 py-10">
        <CardError message="We couldn't find this profile." onRetry={retry} />
      </div>
    );
  }

  if (isLoading || !summary) {
    return <ProfileSkeleton />;
  }

  return (
    <div className="mx-auto w-full max-w-5xl px-4 py-6 sm:py-8">
      <Overview summary={summary} />

      <div className="mt-6 grid gap-6 lg:grid-cols-[2fr_1fr]">
        <div className="flex flex-col gap-6">
          <EventsSection
            heading="Upcoming events"
            empty="No upcoming events."
            events={upcoming.events}
            isLoading={upcoming.isLoading}
            error={upcoming.error}
            onRetry={upcoming.retry}
          />
          <EventsSection
            heading="Events created"
            empty="No public events yet."
            events={hosted.events}
            isLoading={hosted.isLoading}
            error={hosted.error}
            onRetry={hosted.retry}
          />
          <EventsSection
            heading="Joined events"
            empty="No upcoming events joined."
            events={joined.events}
            isLoading={joined.isLoading}
            error={joined.error}
            onRetry={joined.retry}
          />
        </div>

        <ActivityFeed
          activity={activity.activity}
          isLoading={activity.isLoading}
          error={activity.error}
          onRetry={activity.retry}
        />
      </div>
    </div>
  );
}

/**
 * The header: identity, role, bio, and the four numbers.
 *
 * A Creator account gets a visibly heavier treatment - brand-tinted panel, star,
 * larger avatar - because the point of the feature is that a club with forty events
 * behind it should look like one the moment you land, and a Creator badge in the
 * same neutral grey as a Member's would waste the signal.
 */
function Overview({ summary }: { summary: ProfileSummary }) {
  const creator = isCreatorRole(summary.role);
  // The organization name is what a club shows instead of a person's name. Falling
  // back to displayName rather than hiding the line keeps the heading non-empty
  // whichever field the operator filled in.
  const heading = summary.organizationName?.trim() || summary.displayName;

  return (
    <Card
      className={cn(
        "p-5 sm:p-7",
        // Creator gets the brand tint and a brand edge; a Member gets the plain
        // surface every other card in the app uses.
        creator && "border-brand-600/30 bg-brand-tint",
      )}
    >
      <div className="flex flex-wrap items-start gap-4">
        <Avatar
          name={heading}
          src={mediaUrl(summary.avatarUrl)}
          size="lg"
          className={cn("h-16 w-16 sm:h-20 sm:w-20", creator && "ring-2 ring-brand-600/40")}
        />

        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-2">
            <h1 className="text-h1 text-fg">{heading}</h1>
            <RoleBadge role={summary.role} />
          </div>

          {/* The person behind a club name. Rendered only when the heading is
              standing in for an organization, so a normal profile does not repeat
              its own name twice. */}
          {summary.organizationName?.trim() && (
            <p className="mt-1 text-meta text-fg-muted">{summary.displayName}</p>
          )}

          {summary.bio && <p className="mt-3 text-body text-fg-muted">{summary.bio}</p>}

          <p className="mt-3 text-meta text-fg-muted">
            {"Member since "}
            {formatMemberSince(summary.memberSince)}
          </p>
        </div>

        {/* The audience, and the way to join it. Right-aligned so it reads as the
            action column of the header rather than part of the identity block, and
            stacked so the count sits under the button it belongs to. */}
        <div className="flex shrink-0 flex-col items-end gap-2">
          <div className="flex items-center gap-2">
            <MessageButton userId={summary.userId} />
            <FollowButton userId={summary.userId} isFollowing={summary.isFollowing} />
          </div>
          <p className="text-meta text-fg-muted">
            <span className="font-semibold text-fg tabular-nums">
              {summary.followersCount.toLocaleString("en-AU")}
            </span>
            {summary.followersCount === 1 ? " Follower" : " Followers"}
          </p>
        </div>
      </div>

      <dl
        className="mt-6 grid grid-cols-2 gap-3 sm:grid-cols-4"
        aria-label="Profile activity summary"
      >
        {TILES.map(({ key, label }) => (
          <div key={key} className="rounded-md border border-border bg-surface-2 p-3 sm:p-4">
            <dt className="text-meta text-fg-muted">{label}</dt>
            <dd className="mt-1 text-h2 text-fg tabular-nums">{summary.stats[key]}</dd>
          </div>
        ))}
      </dl>
    </Card>
  );
}

/** Year and month only. A profile's tenure is a rough signal, and a precise day
 * invites the reader to treat account age as a score. */
function formatMemberSince(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return "recently";
  return new Intl.DateTimeFormat("en-AU", { month: "long", year: "numeric" }).format(date);
}

/**
 * One event section: heading, count, and the same `EventCard` browse uses.
 *
 * Cards are rendered in the compact `hideImage` form inside a list and never as a
 * bespoke tile - a profile that invents its own event shape is a second card design
 * to keep in sync, and the poster/joined/interested trio the spec asks for is already
 * what EventCard shows.
 */
function EventsSection({
  heading,
  empty,
  events,
  isLoading,
  error,
  onRetry,
}: {
  heading: string;
  empty: string;
  events: EventListItem[] | undefined;
  isLoading: boolean;
  error: unknown;
  onRetry: () => void;
}) {
  return (
    <section aria-label={heading}>
      <div className="flex items-baseline gap-2">
        <h2 className="text-h3 text-fg">{heading}</h2>
        {events && events.length > 0 && (
          <span className="text-meta text-fg-muted tabular-nums">{events.length}</span>
        )}
      </div>

      <div className="mt-3">
        {error ? (
          <Card>
            <div className="p-4">
              <CardError message={`We couldn't load these ${heading.toLowerCase()}.`} onRetry={onRetry} />
            </div>
          </Card>
        ) : isLoading || !events ? (
          <div className="flex flex-col gap-2" aria-busy>
            {[0, 1, 2].map((i) => (
              <SkeletonBlock key={i} className="h-16 w-full" />
            ))}
          </div>
        ) : events.length === 0 ? (
          <p className="rounded-md border border-dashed border-border px-4 py-6 text-center text-meta text-fg-muted">
            {empty}
          </p>
        ) : (
          <div className="flex flex-col gap-2">
            {events.map((event) => (
              <EventCard key={event.id} event={event} hideImage />
            ))}
          </div>
        )}
      </div>
    </section>
  );
}

/** The verb, as the spec words it: "Created", "Hosted", "Joined", "Interested In".
 * A map rather than the raw enum name because the feed reads as sentences and
 * "Interested" alone does not. */
const ACTIVITY_VERB: Record<string, string> = {
  Created: "Created",
  Hosted: "Hosted",
  Joined: "Joined",
  Interested: "Interested in",
};

/**
 * Recent activity, newest first.
 *
 * Each row links to the event it names, so the feed is navigation rather than a
 * transcript. The verbs come from the server already restricted to public events;
 * nothing here can widen them.
 */
function ActivityFeed({
  activity,
  isLoading,
  error,
  onRetry,
}: {
  activity: ProfileActivity[] | undefined;
  isLoading: boolean;
  error: unknown;
  onRetry: () => void;
}) {
  return (
    <Card className="p-4 sm:p-5">
      <h2 className="text-h3 text-fg">Activity</h2>

      <div className="mt-3">
        {error ? (
          <CardError message="We couldn't load this activity." onRetry={onRetry} />
        ) : isLoading || !activity ? (
          <div className="flex flex-col gap-3" aria-busy>
            {[0, 1, 2, 3, 4].map((i) => (
              <SkeletonBlock key={i} className="h-10 w-full" />
            ))}
          </div>
        ) : activity.length === 0 ? (
          <p className="text-meta text-fg-muted">No recent activity.</p>
        ) : (
          <ul className="flex flex-col gap-3">
            {activity.map((entry) => (
              <li key={`${entry.kind}-${entry.eventId}`} className="text-meta">
                <span className="text-fg-muted">
                  {ACTIVITY_VERB[entry.kind] ?? entry.kind}:
                </span>
                <Link
                  href={`/events/${entry.eventId}`}
                  className="mt-0.5 block truncate text-fg hover:text-brand-600 hover:underline"
                >
                  {entry.eventTitle}
                </Link>
              </li>
            ))}
          </ul>
        )}
      </div>
    </Card>
  );
}

/** Header-shaped placeholder, so the page does not collapse and re-expand around a
 * reader who lands on a slow profile. */
function ProfileSkeleton() {
  return (
    <div className="mx-auto w-full max-w-5xl px-4 py-6 sm:py-8" aria-busy>
      <Card className="p-5 sm:p-7">
        <div className="flex items-start gap-4">
          <SkeletonBlock className="h-16 w-16 rounded-full sm:h-20 sm:w-20" />
          <div className="flex-1">
            <SkeletonBlock className="h-7 w-48" />
            <SkeletonBlock className="mt-3 h-4 w-32" />
          </div>
        </div>
        <div className="mt-6 grid grid-cols-2 gap-3 sm:grid-cols-4">
          {[0, 1, 2, 3].map((i) => (
            <SkeletonBlock key={i} className="h-16 w-full" />
          ))}
        </div>
      </Card>
    </div>
  );
}
