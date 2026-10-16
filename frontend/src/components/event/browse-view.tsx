"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useCallback, useEffect, useMemo, useState, useTransition } from "react";
import { EventCardMemo } from "@/components/event/event-card-memo";
import {
  cardPropsById,
  cardPropsOf,
  shouldVirtualize,
  SPLIT_LIST_WIDTH,
  SPLIT_MAP_WIDTH,
  type BrowseLayoutMode,
  type BrowseMapMode,
} from "@/components/event/browse-layout";
import { VirtualizedCardList } from "@/components/event/virtualized-card-list";
import { FilterBar } from "@/components/event/filter-bar";
import { EventMap } from "@/components/map/event-map";
import { EmptyState, ErrorState } from "@/components/state/empty-error";
import { LG_MIN_WIDTH_QUERY, useMediaQuery } from "@/lib/use-media-query";
import { Button } from "@/components/ui/button";
import { Dialog } from "@/components/ui/dialog";
import { EventCardListSkeleton } from "@/components/ui/skeleton";
import { useEvents, useFollowingEvents, useJoinedEvents, usePopularTags } from "@/features/events/use-events";
import { useGeolocation } from "@/features/events/use-geolocation";
import { useAuth } from "@/lib/auth/auth-provider";
import { signInReturnTo } from "@/lib/auth/types";
import { patchQuery, writeQuery } from "@/lib/query-nav";
import { parseQuery, useSearchParamsValue } from "@/lib/query";
import { cn } from "@/lib/cn";
import type { EventQuery } from "@/types/events";

/**
 * `/` Browse (spec §8): FilterBar, then `EventCardList | EventMap`.
 *  - `>= lg`: 60% list column + 40% map, each scrolling on its own inside
 *    `100dvh` (spec §6). The split is percentage-based rather than the spec's
 *    original `420px` column so the list keeps pace with wide displays.
 *  - `< md`: card list + a collapsed map sheet that expands to 60vh.
 *  - first paint: filter bar + 6 skeletons + map placeholder (never a spinner).
 *  - geolocation denied -> Melbourne CBD + a one-line note, never a modal.
 *
 * The URL is the only place filter state lives. `useSearchParamsValue` subscribes
 * to `popstate`, so a chip tap, a Reset, and the browser Back button all arrive
 * through the same path — an earlier version took `searchParams` as a server prop
 * and never re-read it, which made every filter change a no-op.
 */
export function BrowseView() {
  const params = useSearchParamsValue();
  const query = parseQuery(params);
  const router = useRouter();
  const [isPending, startTransition] = useTransition();
  const browse = useEvents(params);
  const followingTabActive = query.tab === "following";
  const { user: authUser } = useAuth();
  const following = useFollowingEvents(params, followingTabActive);
  const joined = useJoinedEvents();
  // One list for the whole view: the Following tab's rows when active, the
  // browse rows otherwise. Everything downstream (selection, map, empty state) reads `items` and so applies to both feeds unchanged — which is
  // the point of narrowing the query server-side rather than forking the page.
  const { items, totalCount, error, isLoading, mutate } = followingTabActive ? following : browse;
  const { tags: popularTags } = usePopularTags();
  const geo = useGeolocation();
  const [selectedEventId, setSelectedEventId] = useState<string | null>(null);
  const [mapSheetOpen, setMapSheetOpen] = useState(false);
  // Desktop map mode, toggled from the filter bar. "none" is the default now: the
  // split used to be permanently open, which spent 40% of the viewport on a map
  // most browsing never looks at. "split" is that old layout, still one tap away.
  // The mobile sheet keeps its own open state - it is a different control.
  //
  // This is also what decides what a card tap does: with the map open a tap
  // focuses the event on the map, with it closed a tap opens the event.
  // See `selectEvent`.
  const [mapMode, setMapMode] = useState<BrowseMapMode>("none");
  // List vs grid. Local state rather than a URL param on purpose: it is a display
  // preference, not a description of which events are being looked at, so it must
  // not become a history entry and must survive Back.
  const [mode, setMode] = useState<BrowseLayoutMode>("list");
  // The `lg` breakpoint: the map column only renders from here up, so below it
  // the list is never beside a visible map. See `selectEvent`.
  const isWide = useMediaQuery(LG_MIN_WIDTH_QUERY);
  const mapVisible = isWide && mapMode !== "none";

  // Selection is browse-local state keyed by event id: it is not a filter, so it
  // deliberately stays out of the URL and survives nothing but a page reload.
  // The map renders its popup from the id against the live list, so an event
  // that disappears underneath the selection simply loses its popup.

  // Both are memoised because every card sits behind a memo boundary, and a
  // callback that is new each render would make that boundary useless: the list
  // would re-render all ~100 cards on any state change in this component, which
  // is precisely the re-render cost the virtualisation exists to avoid.
  //
  // What a tap means depends on whether the map is visible.
  //
  // Map visible: a tap focuses the event on the map - pans to its pin and opens
  // the pin's popup - and a second tap on the same card opens it (the two-stage
  // contract EventCard implements). The popup itself also opens the event.
  //
  // Map hidden (including every narrow viewport, where the map lives in a sheet
  // that covers the list): there is nothing to focus, so a tap goes straight to
  // the event. Selecting would look like a dead click.
  const selectEvent = useCallback(
    (id: string) => {
      if (!mapVisible) {
        router.push(`/events/${id}`);
        return;
      }
      setSelectedEventId(id);
    },
    [mapVisible, router],
  );
  const openEvent = useCallback(
    (id: string) => router.push(`/events/${id}`),
    [router],
  );

  // Ids and payloads are separated so the windowed list compares ids only. The
  // payload map is rebuilt from picked fields, which is what lets the memo
  // comparison be about content rather than object identity.
  const ids = useMemo(() => (items ?? []).map((event) => event.id), [items]);
  const cardData = useMemo(() => cardPropsById(items ?? []), [items]);


  /**
   * A filter change drops the selection. The selected event may simply not be in
   * the new result set, and a popup describing an event that is no longer
   * listed - while its card is nowhere to highlight - is worse than no selection.
   *
   * Done in the handlers rather than an effect on `params` because every filter
   * change in this view is a user action that lands here: `patch`, `reset`, and
   * the back button all end in a `popstate` dispatch, and the back button is
   * handled by the subscription below. React 19 treats a setState in an effect
   * body as a cascading render, and here it genuinely is one - clearing in the
   * same click that changed the filter is a single render instead of two.
   */
  function clearSelection() {
    setSelectedEventId(null);
  }

  function patch(partial: Partial<EventQuery>) {
    clearSelection();
    navigate(partial);
  }
  // The URL half of `patch`. The dispatched `popstate` reaches the listener below,
  // which clears the selection on its own - so this is what an effect calls, where
  // a synchronous setState would be a cascading render.
  function navigate(partial: Partial<EventQuery>) {
    const next = patchQuery(query, partial);
    startTransition(() => {
      window.history.pushState(null, "", `/${writeQuery(next)}`);
      window.dispatchEvent(new PopStateEvent("popstate"));
    });
  }
  function reset() {
    clearSelection();
    startTransition(() => {
      window.history.pushState(null, "", "/");
      window.dispatchEvent(new PopStateEvent("popstate"));
    });
  }

  // Back/forward changes the filters without going through `patch`, so the
  // selection has to be dropped here too. The listener only fires on a real
  // navigation, which is exactly the moment to reset - and unlike an effect on
  // `params`, it never runs on an ordinary re-render.
  useEffect(() => {
    window.addEventListener("popstate", clearSelection);
    return () => window.removeEventListener("popstate", clearSelection);
  }, []);

  // Sort flips to `distance` once a location exists and a radius is active
  // (spec §8). Deliberately after render, never while the user is mid-tap.
  useEffect(() => {
    if (geo.status === "granted" && query.radiusKm && query.sort !== "distance") {
      navigate({ sort: "distance" });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [geo.status, query.radiusKm]);

  const heading = headingFor(query);
  const count = items?.length ?? 0;
  const showEmpty = !isLoading && !error && count === 0;
  // Signed-in is a precondition of the Following feed, and the auth session
  // resolves a beat after first paint. Until it settles, the tab shows the
  // skeleton rather than flashing a sign-in prompt at a user who is signed in.
  const signedOut = authUser === null;

  const listBody = (
    <>
      {followingTabActive && signedOut && (
        // No events, no fetch — the endpoint is viewer-scoped and would only
        // answer 401. The prompt returns to the Following tab after sign-in, and
        // "Discover creators" is the way out: follow someone from the ordinary
        // feed first, which is what gives this tab anything to show.
        <EmptyState
          title="Sign in to see events from people you follow."
          action={
            <div className="flex flex-wrap items-center gap-2">
              <Button onClick={() => router.push(signInReturnTo("/?tab=following"))}>
                Sign in
              </Button>
              <Button variant="secondary" onClick={() => patch({ tab: "all" })}>
                Discover creators
              </Button>
            </div>
          }
        />
      )}
      {isLoading && !items && <EventCardListSkeleton count={6} />}
      {error && (
        <ErrorState
          detail={String(error)}
          onRetry={() => void mutate()}
          className="mx-4 border-0 bg-transparent p-0 lg:mx-0"
        />
      )}
      {followingTabActive && !signedOut && showEmpty && (
        <EmptyState
          title="No events from people you follow yet."
          action={
            <Button variant="secondary" onClick={() => patch({ tab: "all" })}>
              Discover creators
            </Button>
          }
        />
      )}
      {!followingTabActive && showEmpty && (
        <EmptyState
          title={`No ${heading} right now.`}
          action={
            <Link href="/create" className="no-underline">
              <Button>Create the first one</Button>
            </Link>
          }
        />
      )}
      {mode === "grid" ? (
        // Grid: 2-3 columns of the same compact card. Not windowed - three
        // columns of short cards means a 100-row page is ~34 rows deep, which
        // does not justify the fixed-height assumption.
        <div className="grid grid-cols-1 gap-3 md:grid-cols-2 xl:grid-cols-3">
          {items?.map((event) => (
            <EventCardMemo
              key={event.id}
              data={cardData.get(event.id) ?? cardPropsOf(event)}
              selected={event.id === selectedEventId}
              isJoined={joined.isJoined(event.id)}
              onSelect={selectEvent}
              onOpen={openEvent}
              density="compact"
            />
          ))}
        </div>
      ) : shouldVirtualize(count) ? (
        // Long list: windowed, so first paint and every selection touch the ~12
        // rows on screen instead of all 100.
        <VirtualizedCardList
          ids={ids}
          data={cardData}
          renderItem={(id, data) =>
            data ? (
              <EventCardMemo
                data={data}
                selected={id === selectedEventId}
                isJoined={joined.isJoined(id)}
                onSelect={selectEvent}
                onOpen={openEvent}
                density="compact"
              />
            ) : null
          }
        />
      ) : (
        items?.map((event) => (
          <EventCardMemo
            key={event.id}
            data={cardData.get(event.id) ?? cardPropsOf(event)}
            selected={event.id === selectedEventId}
            isJoined={joined.isJoined(event.id)}
            onSelect={selectEvent}
            onOpen={openEvent}
            density="compact"
          />
        ))
      )}
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
      <div
        className={cn(
          "flex min-h-0 flex-col lg:min-w-0 lg:overflow-y-auto",
          // The list owns the screen only when the map is closed; with the map
          // open the two columns split the viewport evenly.
          mapMode === "none"
            ? "lg:w-full"
            : cn(SPLIT_LIST_WIDTH, "lg:border-r lg:border-border"),
        )}
      >
        <h2 className="sr-only">{heading}</h2>
        <FilterBar
          query={query}
          onPatch={patch}
          onReset={reset}
          popularTags={popularTags}
          mapMode={mapMode}
          onMapMode={setMapMode}
          mode={mode}
          onMode={setMode}
        />
        {geo.status === "denied" && (
          <p className="px-4 py-2 text-meta text-fg-muted" role="status">
            Turn on location to sort by distance.
          </p>
        )}
        <div
          className={cn(
            "pb-8 pt-4",
            // Tighter than the old gap-4: the compact card carries its own
            // internal spacing, and a 16px gutter between 90px cards is most of
            // the whitespace the redesign set out to reclaim.
            mode === "grid" ? "flex flex-col gap-3 px-4 lg:px-6" : "flex flex-col gap-2 px-4 lg:px-6",
          )}
        >
          {listBody}
        </div>
      </div>

      {/* The map column, which exists only while the map is open - with the map
          off the list owns the whole viewport, the default and the point of the
          redesign.

          The map fills the column top to bottom, which is what "show the map"
          asks for. It used to be a fixed 160/256px strip with a preview stacked
          above it, so the map read as a footnote parked in the corner. The
          selected event's details live in the pin's own popup, so the map gives
          up no height to them. */}
      {mapMode !== "none" && (
        <div
          className={cn(
            "hidden min-h-0 min-w-0 flex-col gap-3 p-3 lg:flex",
            SPLIT_MAP_WIDTH,
          )}
        >
          {/* The map owns the column top to bottom. */}
          <div className="relative min-h-0 flex-1 overflow-hidden rounded-md border border-border">
            <EventMap
              events={items ?? []}
              selectedEventId={selectedEventId}
              onSelect={setSelectedEventId}
              onOpen={openEvent}
              className="h-full"
            />
          </div>
        </div>
      )}

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
            onSelect={setSelectedEventId}
            onOpen={openEvent}
          />
        </div>
      </Dialog>
    </div>
  );
}

/** The empty-state sentence must echo the active filters (spec §7/§9). */
function headingFor(query: EventQuery): string {
  const bits: string[] = [query.tag ? `#${query.tag}` : "events"];
  if (query.radiusKm) bits.push(`within ${query.radiusKm}km`);
  if (query.date === "today") bits.push("today");
  else if (query.date === "week") bits.push("this week");
  if (query.q) bits.push(`matching "${query.q}"`);
  return bits.join(" ");
}

