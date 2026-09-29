"use client";

import { useMemo, useState } from "react";
import { EventCard } from "@/components/event/event-card";
import { CardActions } from "@/components/event/card-actions";
import { EmptyState, ErrorState } from "@/components/state/empty-error";
import { EventCardListSkeleton } from "@/components/ui/skeleton";
import { useMyEvents } from "@/features/events/use-events";
import { useNow } from "@/lib/use-now";
import { cn } from "@/lib/cn";
import type { EventListItem } from "@/types/events";

type Tab = "upcoming" | "past";

const TABS: { key: Tab; label: string }[] = [
  { key: "upcoming", label: "Upcoming" },
  { key: "past", label: "Past" },
];

/**
 * `/my-events` (spec §8): only events the viewer has a personal relation to —
 * Interested (localStorage) or Joined (a real participant row). The whole feed is
 * not "mine", so it is not listed here. Tabs carry counts in their labels.
 *
 * Each card's actions reflect how it got here: a joined event offers Leave,
 * anything else offers Interest/Uninterest plus Join/Leave. Resolving ids hits
 * `GET /api/events/{id}`, so a past event you joined still appears (browse would
 * have dropped it).
 */
export function MyEventsView() {
  const [tab, setTab] = useState<Tab>("upcoming");
  const my = useMyEvents();
  const now = useNow(60_000);

  const buckets = useMemo(() => {
    const all = my.items.map((item) => ({ item, future: Date.parse(item.startAt) >= now }));
    return {
      upcoming: all.filter((e) => e.future),
      past: all.filter((e) => !e.future),
    };
  }, [my.items, now]);

  const counts: Record<Tab, number> = {
    upcoming: buckets.upcoming.length,
    past: buckets.past.length,
  };

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
        {my.isLoading && <EventCardListSkeleton count={3} />}
        {my.error && <ErrorState detail={String(my.error)} />}
        {!my.isLoading && !my.error && (
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

/** Per-tab empty copy (spec §9) - one sentence, no illustration. */
function emptyCopyFor(tab: Tab): string {
  switch (tab) {
    case "upcoming":
      return "Nothing you're interested in or joined yet. Tap Interested or Join on any event.";
    case "past":
      return "Nothing here yet. Past games you were interested in or joined show up here.";
  }
}

