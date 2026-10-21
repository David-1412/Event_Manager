"use client";

import { useRouter } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import useSWR, { useSWRConfig } from "swr";
import useSWRInfinite from "swr/infinite";
import { toast } from "@/components/ui/toast";
import { fetcher } from "@/lib/swr-fetcher";
import { useAuth } from "@/lib/auth/auth-provider";
import { signInReturnTo } from "@/lib/auth/types";
import type {
  ForumCategory,
  ForumInterestResult,
  ForumPost,
  ForumPostPage,
  ForumQuery,
  TrendingItem,
  TrendingWindow,
} from "@/types/forum";
import {
  forumCategoriesKey,
  forumPostKey,
  forumPostsKey,
  isForumPostsKey,
  recordForumView,
  setForumInterest,
  trendingKey,
} from "./forum-api";

const options = { revalidateOnFocus: false } as const;

export function useForumCategories() {
  const { data, error, isLoading } = useSWR(forumCategoriesKey(), (key: string) =>
    fetcher<ForumCategory[]>(key), options);
  return { categories: data, error, isLoading };
}

/**
 * The feed, page by page. Each page's key is built from the previous page's
 * `nextCursor`, and a null cursor ends the list - which is the server's keyset
 * paging expressed in SWR's terms, so "Load more" never repeats or skips a post
 * when new ones arrive at the top.
 */
export function useForumFeed(query: ForumQuery) {
  const { user } = useAuth();
  const viewerId = user?.id;
  const { data, error, isLoading, isValidating, size, setSize, mutate } = useSWRInfinite(
    (index: number, previous: ForumPostPage | null) => {
      // Following and My Posts are views of the caller's own graph; signed out there
      // is nothing to ask for, and the page shows a sign-in prompt instead.
      if (query.tab !== "all" && !viewerId) return null;
      if (previous && !previous.nextCursor) return null;
      return forumPostsKey(query, index === 0 ? null : (previous?.nextCursor ?? null), viewerId);
    },
    (key: string) => fetcher<ForumPostPage>(key),
    { ...options, revalidateFirstPage: false },
  );

  const posts = data?.flatMap((page) => page.items);
  const hasMore = Boolean(data?.[data.length - 1]?.nextCursor);
  const isLoadingMore = isValidating && size > (data?.length ?? 0);

  return {
    posts,
    error,
    isLoading,
    hasMore,
    isLoadingMore,
    loadMore: () => void setSize(size + 1),
    retry: () => void mutate(),
  };
}

/** Campus Pulse for one window. Refreshes every minute while open: the list is
 * about what is happening now, and the rolling widget would otherwise show a
 * morning's ranking all afternoon. */
export function useTrending(window: TrendingWindow) {
  const { data, error, isLoading } = useSWR(
    trendingKey(window),
    (key: string) => fetcher<TrendingItem[]>(key),
    { ...options, refreshInterval: 60_000 },
  );
  return { items: data, error, isLoading };
}

/**
 * "Interested" on one post.
 *
 * The button shows the server's settled answer, not a guess: the write returns
 * `{ isInterested, likeCount }` recounted from the database, and that answer is held
 * here until the cached feeds catch up (they are revalidated in the background).
 * Signed out, the tap goes to sign-in and back, as Follow and event Interested do.
 */
export function useForumPostInterest(post: ForumPost) {
  const { user } = useAuth();
  const router = useRouter();
  const { mutate } = useSWRConfig();
  const [settled, setSettled] = useState<ForumInterestResult | null>(null);
  const [pending, setPending] = useState(false);

  const state = settled ?? { isInterested: post.isInterested, likeCount: post.likeCount };

  async function toggle() {
    if (!user) {
      router.push(signInReturnTo(window.location.pathname + window.location.search));
      return;
    }
    setPending(true);
    try {
      setSettled(await setForumInterest(post.id, !state.isInterested));
      void mutate(isForumPostsKey);
      void mutate((key) => typeof key === "string" && key.includes("/api/trending"));
    } catch {
      toast("Couldn't update. Try again.", "info");
    } finally {
      setPending(false);
    }
  }

  return { ...state, pending, toggle };
}

/**
 * Count one view of the post when its page opens, once per page load (a ref guards
 * React's development double-run), after the session has resolved so a signed-in
 * reader's view is attributed to them. The result is written into the cached post:
 * the view total, and the Interested state a first view may have just set. The post
 * is then re-read, so a page whose own fetch was still in flight cannot keep showing
 * the state from before.
 */
export function useRecordPostView(id: string) {
  const { user, loading } = useAuth();
  const { mutate } = useSWRConfig();
  const recorded = useRef<string | null>(null);

  useEffect(() => {
    if (!id || loading || recorded.current === id) return;
    recorded.current = id;
    recordForumView(id)
      .then((result) => {
        void mutate(
          forumPostKey(id, user?.id),
          (current: ForumPost | undefined) =>
            current
              ? { ...current, viewCount: result.viewCount, isInterested: result.isInterested, likeCount: result.likeCount }
              : current,
          { revalidate: true },
        );
        void mutate(isForumPostsKey);
        void mutate((key) => typeof key === "string" && key.includes("/api/trending"));
      })
      .catch(() => undefined); // a view that could not be counted never breaks the page
  }, [id, loading, user?.id, mutate]);
}

export function useForumPost(id: string) {
  const { user } = useAuth();
  const { data, error, isLoading, mutate } = useSWR(
    id ? forumPostKey(id, user?.id) : null,
    (key: string) => fetcher<ForumPost>(key),
    options,
  );
  return { post: data, error, isLoading, retry: () => void mutate() };
}
