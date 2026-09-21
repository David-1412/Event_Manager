"use client";

import Link from "next/link";
import { useEffect, useMemo, useState, useTransition } from "react";
import { EventCard } from "@/components/event/event-card";
import { FilterBar } from "@/components/event/filter-bar";
import { EventMap } from "@/components/map/event-map";
import { EmptyState, ErrorState } from "@/components/state/empty-error";
import { Button } from "@/components/ui/button";
import { Dialog } from "@/components/ui/dialog";
import { EventCardListSkeleton } from "@/components/ui/skeleton";
import { useEvents } from "@/features/events/use-events";
import { useGeolocation } from "@/features/events/use-geolocation";
import { patchQuery, readQuery, writeQuery } from "@/lib/query-nav";
import type { EventQuery } from "@/types/events";

/**
 * `/` Browse (spec §8): FilterBar, then `EventCardList | EventMap`.
 *  - `>= lg`: 420px list column + map filling the rest, each scrolling on its
 *    own inside `100dvh` (spec §6).
 *  - `< md`: card list + a collapsed map sheet that expands to 60vh.
 *  - first paint: filter bar + 6 skeletons + map placeholder (never a spinner).
 *  - geolocation denied -> Melbourne CBD + a one-line note, never a modal.
 */
export function BrowseView({ search }: { search: string }) {
  const query = useMemo(() => readQuery(new URLSearchParams(search)), [search]);
  const [isPending, startTransition] = useTransition();
  const { items, totalCount, error, isLoading, mutate } = useEvents(
    new URLSearchParams(search),
  );
  const geo = useGeolocation();
  const [selectedEventId, setSelectedEventId] = useState<string | null>(null);
  const [mapSheetOpen, setMapSheetOpen] = useState(false);

  function patch(partial: Partial<EventQuery>) {
    const next = patchQuery(query, partial);
    startTransition(() => {
      window.history.pushState(null, "", `/${writeQuery(next)}`);
      window.dispatchEvent(new PopStateEvent("popstate"));
    });
  }
  function reset() {
    startTransition(() => {
      window.history.pushState(null, "", "/");
      window.dispatchEvent(new PopStateEvent("popstate"));
    });
  }

  // Sort flips to `distance` once a location exists and a radius is active
  // (spec §8). Deliberately after render, never while the user is mid-tap.
  useEffect(() => {
    if (geo.status === "granted" && query.radiusKm && query.sort !== "distance") {
      patch({ sort: "distance" });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [geo.status, query.radiusKm]);

  const heading = headingFor(query);
  const count = items?.length ?? 0;
  const showEmpty = !isLoading && !error && count === 0;

  const listBody = (
    <>
      {isLoading && !items && <EventCardListSkeleton count={6} />}
      {error && (
        <ErrorState
          detail={String(error)}
          onRetry={() => void mutate()}
          className="mx-4 border-0 bg-transparent p-0 lg:mx-0"
        />
      )}
      {showEmpty && (
        <EmptyState
          title={`No ${heading} right now.`}
          action={
            <Link href="/create" className="no-underline">
              <Button>Create the first one</Button>
            </Link>
          }
        />
      )}
      {items?.map((event) => (
        <div key={event.id} className="px-4 lg:px-0">
          <EventCard
            event={event}
            selected={event.id === selectedEventId}
            onSelect={setSelectedEventId}
          />
        </div>
      ))}
      {count > 0 && totalCount !== undefined && (
        <p className="px-4 py-2 text-meta text-fg-muted lg:px-0" aria-live="polite">
          Showing {count} of {totalCount}
          {isPending ? "…" : ""}
        </p>
      )}
    </>
  );

  return (
    <div className="flex flex-1 flex-col lg:h-[calc(100dvh-3.5rem)] lg:flex-row lg:overflow-hidden">
      <div className="flex flex-col lg:w-[420px] lg:shrink-0 lg:overflow-y-auto lg:border-r lg:border-border">
        <h1 className="sr-only">{heading}</h1>
        <FilterBar query={query} onPatch={patch} onReset={reset} />
        {geo.status === "denied" && (
          <p className="px-4 py-2 text-meta text-fg-muted" role="status">
            Turn on location to sort by distance.
          </p>
        )}
        <div className="flex flex-col gap-4 pb-8 pt-4">{listBody}</div>
      </div>

      {/* Desktop map fills the rest and scrolls independently (spec §6). */}
      <div className="hidden lg:block lg:min-w-0 lg:flex-1">
        <EventMap
          events={items ?? []}
          selectedEventId={selectedEventId}
          onSelect={setSelectedEventId}
        />
      </div>

      {/* Mobile collapsed map sheet expanding to 60vh (spec §6). */}
      <div className="sticky bottom-0 z-20 lg:hidden">
        <button
          type="button"
          onClick={() => setMapSheetOpen(true)}
          className="press flex h-12 w-full items-center justify-center gap-2 border-t border-border bg-surface text-meta font-medium text-fg"
          aria-haspopup="dialog"
          aria-expanded={mapSheetOpen}
        >
          <span aria-hidden>{"\u25C6"}</span> Map
        </button>
      </div>
      <Dialog
        open={mapSheetOpen}
        onClose={() => setMapSheetOpen(false)}
        labelledBy="map-sheet-title"
        variant="sheet"
      >
        <h2 id="map-sheet-title" className="mb-2 text-h3 text-fg">
          Map of events
        </h2>
        <div className="h-[52vh]">
          <EventMap
            events={items ?? []}
            selectedEventId={selectedEventId}
            onSelect={(id) => {
              setSelectedEventId(id);
              if (id) setMapSheetOpen(false);
            }}
          />
        </div>
      </Dialog>
    </div>
  );
}

/** The empty-state sentence must echo the active filters (spec §7/§9). */
function headingFor(query: EventQuery): string {
  const bits: string[] = [query.sport ? query.sport : "events"];
  if (query.radiusKm) bits.push(`within ${query.radiusKm}km`);
  if (query.date === "today") bits.push("today");
  else if (query.date === "week") bits.push("this week");
  if (query.q) bits.push(`matching "${query.q}"`);
  return bits.join(" ");
}

