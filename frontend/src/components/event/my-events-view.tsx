"use client";

import { useMemo, useState } from "react";
import { EventCard } from "@/components/event/event-card";
import { CardActions } from "@/components/event/card-actions";
import { EmptyState, ErrorState } from "@/components/state/empty-error";
import { EventCardListSkeleton } from "@/components/ui/skeleton";
import { useHostedEvents, useMyEvents } from "@/features/events/use-events";
import { useNow } from "@/lib/use-now";
import { cn } from "@/lib/cn";
import type { EventListItem } from "@/types/events";
import { isPendingReviewStatus } from "@/types/events";
import { HostingActions } from "@/components/event/hosting-actions";
import { HostedEventEditor } from "@/components/event/hosted-event-editor";
import { useAuth } from "@/lib/auth/auth-provider";

type Tab = "upcoming" | "past" | "hosting";

const TABS: { key: Tab; label: string }[] = [
  { key: "upcoming", label: "Upcoming" },
  { key: "past", label: "Past" },
  { key: "hosting", label: "Hosting" },
];

/**
 * `/my-events` (spec §8): only events the viewer has a personal relation to —
 * Interested (localStorage) or Joined (a real participant row). The whole feed is
 * not "mine", so it is not listed here. Upcoming and Past bucket those events by
 * date and already cover both relations, so there is no separate Interested tab.
 * Tabs carry counts in their labels.
 *
 * Each card's actions reflect how it got here: a joined event offers Leave,
 * anything else offers Interest/Uninterest plus Join/Leave. Resolving ids hits
 * `GET /api/events/{id}`, so a past event you joined still appears (browse would
 * have dropped it).
 */
export function MyEventsView() {
  const [tab, setTab] = useState<Tab>("upcoming");
  const { canPublishPublicEvents } = useAuth();
  const my = useMyEvents();
  const hosted = useHostedEvents();
  const now = useNow(60_000);
  // Stable reference for the buckets memo (the `my` object is fresh each render).
  const { items } = my;

  const buckets = useMemo(() => {
    const all = items.map((item) => ({ item, future: Date.parse(item.startAt) >= now }));
    return {
      upcoming: all.filter((e) => e.future),
      past: all.filter((e) => !e.future),
    };
  }, [items, now]);

  const counts: Record<Tab, number> = {
    upcoming: buckets.upcoming.length,
    past: buckets.past.length,
    hosting: hosted.items.length,
  };
  const isLoading = tab === "hosting" ? hosted.isLoading : my.isLoading;
  const error = tab === "hosting" ? hosted.error : my.error;

  return (
    <div className="mx-auto w-full max-w-[680px] px-4 py-4">
      {my.slots}
      <h1 className="text-h1 text-fg">My events</h1>
      <p className="mt-1 text-meta text-fg-muted">
        Events you&apos;re interested in or have joined.
      </p>

      <div role="tablist" aria-label="My events" className="mt-4 flex gap-1 border-b border-border">
        {TABS.map((entry) => (
          <button
            key={entry.key}
            role="tab"
            type="button"
            aria-selected={tab === entry.key}
            aria-controls={`panel-${entry.key}`}
            id={`tab-${entry.key}`}
            onClick={() => setTab(entry.key)}
            className={cn(
              "press -mb-px rounded-t-md border-b-2 px-3 py-2 text-meta font-medium",
              tab === entry.key
                ? "border-brand-600 text-brand-600"
                : "border-transparent text-fg-muted hover:text-fg",
            )}
          >
            {entry.label} ({counts[entry.key]})
          </button>
        ))}
      </div>

      <div
        role="tabpanel"
        id={`panel-${tab}`}
        aria-labelledby={`tab-${tab}`}
        className="mt-4 flex flex-col gap-4"
      >
        {isLoading && <EventCardListSkeleton count={3} />}
        {error && <ErrorState detail={String(error)} />}
        {!isLoading && !error && tab === "hosting" && (
          <HostingPanel items={hosted.items} now={now} canPublishPublicEvents={canPublishPublicEvents} />
        )}
        {!isLoading && !error && tab !== "hosting" && (
          <TabPanel tab={tab} rows={buckets[tab]} my={my} />
        )}
      </div>
    </div>
  );
}

function TabPanel({
  tab,
  rows,
  my,
}: {
  tab: Tab;
  rows: { item: EventListItem }[];
  my: ReturnType<typeof useMyEvents>;
}) {
  if (rows.length === 0) return <EmptyState title={emptyCopyFor(tab)} />;

  return (
    <>
      {rows.map(({ item }) => (
        <EventCard
          key={item.id}
          event={item}
          isJoined={my.isJoined(item.id)}
          action={<CardActions eventId={item.id} />}
        />
      ))}
    </>
  );
}

function HostingPanel({ items, now, canPublishPublicEvents }: {
  items: EventListItem[];
  now: number;
  canPublishPublicEvents: boolean;
}) {
  if (items.length === 0) return <EmptyState title="You haven't hosted any events yet." />;

  // Pending first: an event waiting on a decision is the one the host came here to
  // check on, and burying it in start-date order behind next month's already-live
  // event would hide it. The notice is the counterpart to the submit dialog's
  // promise - it is what tells the host, on a later visit, that the wait is still on.
  const pending = items.filter((item) => isPendingReviewStatus(item.status));
  const rest = items.filter((item) => !isPendingReviewStatus(item.status));

  return (
    <>
      {pending.length > 0 && (
        <p
          role="status"
          className="rounded-md border border-warn/40 bg-warn-tint px-4 py-3 text-meta text-fg"
        >
          {pending.length === 1
            ? "1 event is awaiting admin review. It stays hidden from Browse until an administrator approves it."
            : `${pending.length} events are awaiting admin review. They stay hidden from Browse until an administrator approves them.`}
        </p>
      )}
      {[...pending, ...rest].map((item) => {
        const isPending = isPendingReviewStatus(item.status);
        const ended = item.status === "Completed" || Date.parse(item.startAt) <= now;
        // Awaiting review outranks ended/mine. The status is the fact that matters
        // about this card, and eventCardVariant already routes it to the yellow
        // Pending Approval badge and the muted count.
        const variant = isPending
          ? "pending-review"
          : ended
            ? "ended"
            : item.isCancelled
              ? "cancelled"
              : "mine";
        return (
          <EventCard
            key={item.id}
            event={item}
            isHost
            variant={variant}
            action={(
              <div className="flex flex-wrap gap-2">
                <HostedEventEditor
                  eventId={item.id}
                  visibility={item.visibility}
                  canPublishPublicEvents={canPublishPublicEvents}
                />
                <HostingActions
                  eventId={item.id}
                  isCancelled={item.isCancelled}
                  isEnded={ended}
                  awaitingReview={isPending}
                />
              </div>
            )}
          />
        );
      })}
    </>
  );
}

/** Per-tab empty copy (spec §9) - one sentence, no illustration. */
function emptyCopyFor(tab: Tab): string {
  switch (tab) {
    case "upcoming":
      return "Nothing you're interested in or joined yet. Tap Interested or Join on any event.";
    case "past":
      return "Nothing here yet. Past Events you were interested in or joined show up here.";
    case "hosting":
      return "You haven't hosted any events yet.";
  }
}
