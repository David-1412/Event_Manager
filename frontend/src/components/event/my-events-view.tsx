"use client";

import { useMemo, useState } from "react";
import { EventCard } from "@/components/event/event-card";
import { EmptyState, ErrorState } from "@/components/state/empty-error";
import { EventCardListSkeleton } from "@/components/ui/skeleton";
import { useEvents } from "@/features/events/use-events";
import { useNow } from "@/lib/use-now";
import { fixtureDetail, fixtureList } from "@/lib/fixtures";
import { cn } from "@/lib/cn";

type Tab = "hosting" | "joined" | "past";

const TABS: { key: Tab; label: string }[] = [
  { key: "hosting", label: "Hosting" },
  { key: "joined", label: "Joined" },
  { key: "past", label: "Past" },
];

/**
 * `/my-events` (spec §8): tabs carry counts in their labels - `Joined (2)` - so
 * the number answers the question before the tab is opened. Each tab filters
 * independently, and a failed fetch only blanks the tab it belongs to.
 *
 * Auth is not wired yet, so the fixture relations (`isHost` / `isJoined`) drive
 * the split; when the real `GET /api/events?scope=` lands, only `buckets` swaps.
 */
export function MyEventsView() {
  const [tab, setTab] = useState<Tab>("hosting");
  const { error, isLoading, mutate } = useEvents();
  const now = useNow(60_000);

  // Auth is not wired yet, so fixture relations drive the split; when
  // `GET /api/events?scope=` lands, only this block swaps. `now` arrives from
  // `useNow` rather than `Date.now()` so the memo stays pure.
  const buckets = useMemo(() => {
    const all = fixtureList().map((item) => ({
      item,
      detail: fixtureDetail(item.id),
    }));
    return {
      hosting: all.filter((e) => e.detail?.isHost && Date.parse(e.item.startAt) >= now),
      joined: all.filter((e) => e.detail?.isJoined && Date.parse(e.item.startAt) >= now),
      past: all.filter((e) => Date.parse(e.item.startAt) < now),
    };
  }, [now]);

  const counts: Record<Tab, number> = {
    hosting: buckets.hosting.length,
    joined: buckets.joined.length,
    past: buckets.past.length,
  };
  const rows = buckets[tab];

  return (
    <div className="mx-auto w-full max-w-[680px] px-4 py-4">
      <h1 className="text-h1 text-fg">My events</h1>

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
        {error && <ErrorState detail={String(error)} onRetry={() => void mutate()} />}
        {!error && rows.length === 0 && (
          <EmptyState title={emptyCopyFor(tab)} />
        )}
        {rows.map(({ item, detail }) => (
          <EventCard
            key={item.id}
            event={item}
            variant={tab === "past" ? "past" : detail?.isHost ? "mine" : "joined"}
            isJoined={detail?.isJoined}
            isHost={detail?.isHost}
          />
        ))}
      </div>
    </div>
  );
}

/** Per-tab empty copy (spec §9) - one sentence, no illustration. */
function emptyCopyFor(tab: Tab): string {
  switch (tab) {
    case "hosting":
      return "You're not hosting anything yet - create one and it shows up here.";
    case "joined":
      return "You haven't joined anything yet. Browse and take a spot.";
    case "past":
      return "Nothing here yet. Past games show up once they've been and gone.";
  }
}
