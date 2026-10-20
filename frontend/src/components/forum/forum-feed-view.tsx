"use client";

import Link from "next/link";
import { useTransition } from "react";
import { Flame } from "lucide-react";
import { PostCard } from "@/components/forum/post-card";
import { TrendingBanner } from "@/components/forum/TrendingBanner";
import { GlobalSearch } from "@/components/layout/global-search";
import { SectionSubnav } from "@/components/layout/section-subnav";
import { useAuth } from "@/lib/auth/auth-provider";
import { signInReturnTo } from "@/lib/auth/types";
import { EmptyState, ErrorState } from "@/components/state/empty-error";
import { Button } from "@/components/ui/button";
import { SkeletonBlock } from "@/components/ui/skeleton";
import { parseForumQuery, writeForumQuery } from "@/features/forum/forum-query";
import { useForumCategories, useForumFeed } from "@/features/forum/use-forum";
import { useSearchParamsValue } from "@/lib/query";
import { cn } from "@/lib/cn";
import type { ForumQuery, ForumSort } from "@/types/forum";

/**
 * `/forum`: the campus feed.
 *
 * Built like Browse so the two sections feel like one product: the URL holds the
 * filters (`useSearchParamsValue` + pushState), category chips sit where Browse's
 * tag chips do, and the list is a single column of cards.
 *
 * Paging is "Load more" over the server's cursors rather than the windowed list
 * Browse uses: that list assumes every row is the same height, and a post's height
 * depends on how much someone wrote.
 */
export function ForumFeedView() {
  const params = useSearchParamsValue();
  const query = parseForumQuery(params);
  const [, startTransition] = useTransition();
  const { categories } = useForumCategories();
  const feed = useForumFeed(query);
  const { user, loading: authLoading } = useAuth();

  function patch(partial: Partial<ForumQuery>) {
    const next = { ...query, ...partial };
    startTransition(() => {
      window.history.pushState(null, "", `/forum${writeForumQuery(next)}`);
      window.dispatchEvent(new PopStateEvent("popstate"));
    });
  }

  const activeCategory = categories?.find((c) => c.slug === query.category);
  const needsSignIn = query.tab !== "all" && !authLoading && !user;

  return (
    <div className="mx-auto w-full max-w-3xl px-4 py-6">
      <h1 className="sr-only">Forum</h1>
      <GlobalSearch context="forum" />

      <SectionSubnav
        label="Forum"
        className="mt-4"
        createFirst="post"
        items={[
          { label: "All", active: query.tab === "all", onSelect: () => patch({ tab: "all" }) },
          { label: "Following", active: query.tab === "following", onSelect: () => patch({ tab: "following" }) },
          { label: "My Posts", active: query.tab === "mine", onSelect: () => patch({ tab: "mine" }) },
        ]}
      />

      <TrendingBanner className="mt-4" />

      {/* Hot / New first, then the category pills: the sort is the feed itself,
          the category narrows it. Sticky so both stay in reach while scrolling. */}
      <div className="sticky top-14 z-10 -mx-4 mt-4 flex flex-col gap-3 border-b border-border bg-surface px-4 pb-3">
        <SortTabs sort={query.sort} onSort={(sort) => patch({ sort })} />
        <div
          role="tablist"
          aria-label="Forum categories"
          className="-mx-1 flex gap-2 overflow-x-auto px-1 pb-1"
        >
          <CategoryChip label="All" selected={!query.category} onClick={() => patch({ category: null })} />
          {categories?.map((c) => (
            <CategoryChip
              key={c.slug}
              label={c.name}
              selected={query.category === c.slug}
              onClick={() => patch({ category: c.slug })}
            />
          ))}
        </div>
      </div>

      <div className="mt-4 flex flex-col gap-3">
        {needsSignIn ? (
          <EmptyState
            title={
              query.tab === "following"
                ? "Sign in to see posts from people you follow."
                : "Sign in to see your posts."
            }
            action={
              <Link href={signInReturnTo(`/forum${writeForumQuery(query)}`)} className="no-underline">
                <Button>Sign in</Button>
              </Link>
            }
          />
        ) : feed.error ? (
          <ErrorState detail={String(feed.error)} onRetry={feed.retry} />
        ) : feed.isLoading || !feed.posts ? (
          [0, 1, 2, 3].map((i) => <SkeletonBlock key={i} className="h-32 w-full" />)
        ) : feed.posts.length === 0 ? (
          <EmptyState
            title={emptyTitle(query, activeCategory?.name)}
            action={
              query.tab === "following" ? (
                <Button variant="secondary" onClick={() => patch({ tab: "all" })}>
                  Browse all posts
                </Button>
              ) : query.q ? (
                <Button variant="secondary" onClick={() => patch({ q: null })}>
                  Clear search
                </Button>
              ) : (
                <Link href="/forum/new" className="no-underline">
                  <Button>{query.tab === "mine" ? "Write your first post" : "Start the first conversation"}</Button>
                </Link>
              )
            }
          />
        ) : (
          <>
            {feed.posts.map((post) => (
              <PostCard key={post.id} post={post} />
            ))}
            {feed.hasMore && (
              <Button
                variant="secondary"
                onClick={feed.loadMore}
                loading={feed.isLoadingMore}
                className="self-center"
              >
                Load more
              </Button>
            )}
          </>
        )}
      </div>
    </div>
  );
}

/** Says why the list is empty in terms of what is selected, so an empty search
 * does not read as an empty forum. */
export function emptyTitle(query: ForumQuery, categoryName?: string): string {
  const where = categoryName ? ` in ${categoryName}` : "";
  if (query.q) return `No posts${where} match "${query.q}".`;
  if (query.tab === "following") return `No posts${where} from people you follow yet.`;
  if (query.tab === "mine") return categoryName ? `You haven't posted in ${categoryName} yet.` : "You haven't posted yet.";
  return `No posts${where} yet.`;
}

function CategoryChip({
  label,
  selected,
  onClick,
}: {
  label: string;
  selected: boolean;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      role="tab"
      aria-selected={selected}
      onClick={onClick}
      className={cn(
        "press h-[34px] shrink-0 rounded-full border px-3 text-meta font-medium",
        selected
          ? "border-brand-600 bg-brand-tint text-brand-600"
          : "border-border bg-surface-2 text-fg hover:bg-surface",
      )}
    >
      {label}
    </button>
  );
}

const SORTS: { value: ForumSort; label: string }[] = [
  { value: "hot", label: "Hot" },
  { value: "new", label: "New" },
];

/** Hot | New as tabs: each is a different ordering of the whole feed, not a filter. */
function SortTabs({ sort, onSort }: { sort: ForumSort; onSort: (sort: ForumSort) => void }) {
  return (
    <div role="tablist" aria-label="Sort posts" className="flex gap-6 border-b border-border">
      {SORTS.map((s) => (
        <button
          key={s.value}
          type="button"
          role="tab"
          aria-selected={sort === s.value}
          onClick={() => onSort(s.value)}
          className={cn(
            "press -mb-px inline-flex h-11 items-center gap-1.5 border-b-2 text-body font-semibold",
            sort === s.value
              ? "border-danger text-danger"
              : "border-transparent text-fg-muted hover:text-fg",
          )}
        >
          {s.value === "hot" && <Flame aria-hidden size={16} className={sort === "hot" ? "fill-current" : ""} />}
          {s.label}
        </button>
      ))}
    </div>
  );
}
