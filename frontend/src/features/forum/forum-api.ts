import { API_BASE_URL, request } from "@/lib/api";
import type {
  CreateForumPost,
  ForumInterestResult,
  ForumPost,
  ForumViewResult,
  ForumQuery,
  TrendingItem,
  TrendingWindow,
} from "@/types/forum";

/**
 * Keys and writes for the campus forum.
 *
 * Post keys carry the viewer, like `profileSummaryKey`, because a post's payload
 * depends on who asks: `isMine` is per caller, and an admin sees the author of an
 * anonymous post that everyone else sees as "Anonymous". An unpartitioned key would
 * hand one account's cached answers to the next person to sign in on the browser.
 * The viewer rides in a `#` fragment, which `fetch` never sends - the server still
 * takes the caller from the bearer token.
 */
const viewerFragment = (viewerId?: string | null) => (viewerId ? `#viewer=${viewerId}` : "");

export const forumCategoriesKey = () => `${API_BASE_URL}/api/forum/categories`;

export function forumPostsKey(
  query: ForumQuery,
  cursor: string | null,
  viewerId?: string | null,
): string {
  const params = new URLSearchParams();
  if (query.category) params.set("category", query.category);
  params.set("sort", query.sort);
  if (query.tab !== "all") params.set("tab", query.tab);
  if (query.q) params.set("q", query.q);
  if (cursor) params.set("cursor", cursor);
  return `${API_BASE_URL}/api/forum/posts?${params.toString()}${viewerFragment(viewerId)}`;
}

export const forumPostKey = (id: string, viewerId?: string | null) =>
  `${API_BASE_URL}/api/forum/posts/${id}${viewerFragment(viewerId)}`;

/** True for every key that holds forum posts, so a new post can refresh them all. */
export const isForumPostsKey = (key: unknown) =>
  typeof key === "string" && key.includes("/api/forum/posts");

export const createForumPost = (body: CreateForumPost) =>
  request<ForumPost>("/api/forum/posts", { method: "POST", body });

/** Record that the post page was opened. On a signed-in reader's first ever view the
 * server also marks them Interested; the result says so. */
export const recordForumView = (id: string) =>
  request<ForumViewResult>(`/api/forum/posts/${id}/view`, { method: "POST" });

/** PUT marks, DELETE clears; both are idempotent and return the settled state. */
export const setForumInterest = (id: string, interested: boolean) =>
  request<ForumInterestResult>(`/api/forum/posts/${id}/interest`, {
    method: interested ? "PUT" : "DELETE",
  });

export const trendingKey = (window: TrendingWindow) => `${API_BASE_URL}/api/trending?window=${window}`;

/** Where a trending row links: the event page or the forum post. */
export const trendingHref = (item: TrendingItem) =>
  item.kind === "event" ? `/events/${item.id}` : `/forum/${item.id}`;
