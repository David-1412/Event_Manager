"use client";

import Link from "next/link";
import { useMemo } from "react";
import { EventCard } from "@/components/event/event-card";
import { PostCard } from "@/components/forum/post-card";
import { searchHref } from "@/components/layout/global-search";
import { EmptyState, ErrorState } from "@/components/state/empty-error";
import { Button } from "@/components/ui/button";
import { SkeletonBlock } from "@/components/ui/skeleton";
import { useForumFeed } from "@/features/forum/use-forum";
import { useEvents } from "@/features/events/use-events";
import { useSearchParamsValue } from "@/lib/query";

/** How many of each kind the combined (All) view shows before "See all". */
const PREVIEW = 5;

/**
 * `/search?q=...&type=all|events|posts`: the results of the global search bar.
 *
 * Both halves come from the endpoints their own pages already use - `GET
 * /api/events?q=` and `GET /api/forum/posts?q=` - so a result here is exactly what
 * that page would show for the same search, and "See all" hands the search over to
 * that page with its filters.
 */
export function SearchResultsView() {
  const params = useSearchParamsValue();
  const q = params.get("q")?.trim() ?? "";
  const typeParam = params.get("type");
  const type = typeParam === "events" || typeParam === "posts" ? typeParam : "all";

  return (
    <div className="mx-auto w-full max-w-3xl px-4 py-6">
      {/* The search box and its All / Events / Posts scope live in the header. */}
      <h1 className="text-h2 text-fg">
        {q ? (
          <>
            Results for &ldquo;{q}&rdquo;
            <span className="ml-2 text-meta font-normal text-fg-muted">
              in {type === "all" ? "events and posts" : type}
            </span>
          </>
        ) : (
          "Search"
        )}
      </h1>

      {!q ? (
        <EmptyState className="mt-6 px-0" title="Use the search in the header to find events and forum posts." />
      ) : (
        <div className="mt-6 flex flex-col gap-8">
          {type !== "posts" && <EventResults q={q} preview={type === "all"} />}
          {type !== "events" && <PostResults q={q} preview={type === "all"} />}
        </div>
      )}
    </div>
  );
}

function SectionHeading({ title, count, seeAll }: { title: string; count?: number; seeAll?: string }) {
  return (
    <div className="mb-3 flex items-baseline gap-2">
      <h2 className="text-h3 text-fg">{title}</h2>
      {count !== undefined && <span className="text-meta text-fg-muted tabular-nums">{count}</span>}
      {seeAll && (
        <Link href={seeAll} className="ml-auto text-meta font-medium text-brand-600">
          See all
        </Link>
      )}
    </div>
  );
}

function EventResults({ q, preview }: { q: string; preview: boolean }) {
  const search = useMemo(() => new URLSearchParams({ q }), [q]);
  const { items, totalCount, error, isLoading, mutate } = useEvents(search);
  const shown = preview ? items?.slice(0, PREVIEW) : items;
  const seeAll = searchHref("events", q, new URLSearchParams(), "search");

  return (
    <section aria-label="Event results">
      <SectionHeading
        title="Events"
        count={totalCount ?? items?.length}
        seeAll={preview && (items?.length ?? 0) > PREVIEW ? seeAll : undefined}
      />
      {error ? (
        <ErrorState detail={String(error)} onRetry={() => void mutate()} />
      ) : isLoading && !items ? (
        <Skeletons />
      ) : !shown || shown.length === 0 ? (
        <p className="text-meta text-fg-muted">No events match &ldquo;{q}&rdquo;.</p>
      ) : (
        <div className="flex flex-col gap-2">
          {shown.map((event) => (
            <EventCard key={event.id} event={event} density="compact" />
          ))}
        </div>
      )}
    </section>
  );
}

function PostResults({ q, preview }: { q: string; preview: boolean }) {
  const query = useMemo(() => ({ category: null, sort: "new" as const, q, tab: "all" as const }), [q]);
  const feed = useForumFeed(query);
  const shown = preview ? feed.posts?.slice(0, PREVIEW) : feed.posts;
  const seeAll = searchHref("posts", q, new URLSearchParams(), "search");
  const more = (feed.posts?.length ?? 0) > PREVIEW || feed.hasMore;

  return (
    <section aria-label="Forum post results">
      <SectionHeading title="Forum posts" seeAll={preview && more ? seeAll : undefined} />
      {feed.error ? (
        <ErrorState detail={String(feed.error)} onRetry={feed.retry} />
      ) : feed.isLoading || !shown ? (
        <Skeletons />
      ) : shown.length === 0 ? (
        <p className="text-meta text-fg-muted">No posts match &ldquo;{q}&rdquo;.</p>
      ) : (
        <div className="flex flex-col gap-3">
          {shown.map((post) => (
            <PostCard key={post.id} post={post} />
          ))}
          {!preview && feed.hasMore && (
            <Button variant="secondary" onClick={feed.loadMore} loading={feed.isLoadingMore} className="self-center">
              Load more
            </Button>
          )}
        </div>
      )}
    </section>
  );
}

function Skeletons() {
  return (
    <div className="flex flex-col gap-2" aria-busy>
      {[0, 1, 2].map((i) => (
        <SkeletonBlock key={i} className="h-24 w-full" />
      ))}
    </div>
  );
}
