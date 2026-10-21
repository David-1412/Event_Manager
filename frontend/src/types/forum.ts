import type { UserRole } from "@/types/admin";

/** Mirrors `ForumCategoryDto`. */
export interface ForumCategory {
  id: number;
  name: string;
  slug: string;
}

/** Mirrors `ForumAuthorDto`. */
export interface ForumAuthor {
  id: string;
  name: string;
  photoUrl: string | null;
  role: UserRole;
}

/** Mirrors `ForumPostDto`. */
export interface ForumPost {
  id: string;
  category: ForumCategory;
  title: string | null;
  /** The full body on the post page; an excerpt in feeds (see `isTruncated`). */
  content: string;
  isTruncated: boolean;
  isAnonymous: boolean;
  /** Null on an anonymous post, unless the viewer wrote it or is an admin. */
  author: ForumAuthor | null;
  isMine: boolean;
  /** Whether the viewer marked it "Interested". Always false signed out. */
  isInterested: boolean;
  /** How many people marked it "Interested" (the forum's like). */
  likeCount: number;
  commentCount: number;
  isPinned: boolean;
  createdAt: string;
  /** Total page views. Only ever rises - it is the Monash Pulse score. */
  viewCount: number;
}

/** Mirrors `ForumViewResultDto`: after opening a post. */
export interface ForumViewResult {
  viewCount: number;
  isInterested: boolean;
  likeCount: number;
}

/** Mirrors `ForumInterestResultDto`: the settled state after a toggle. */
export interface ForumInterestResult {
  isInterested: boolean;
  likeCount: number;
}

/** Mirrors `TrendingItemDto`. */
export interface TrendingItem {
  kind: "event" | "post";
  id: string;
  title: string;
  /** Total page views, all time. Never decreases. */
  score: number;
}

/** The API's `window` values: rolling 24 hours, 72 hours and 7 days. */
export type TrendingWindow = "today" | "3d" | "week";

/** Mirrors `ForumPostPage`. */
export interface ForumPostPage {
  items: ForumPost[];
  nextCursor: string | null;
}

export type ForumSort = "hot" | "new";

/** Mirrors `CreateForumPostDto`. `category` is the slug. */
export interface CreateForumPost {
  category: string;
  title: string | null;
  content: string;
  isAnonymous: boolean;
}

/** Which posts the feed shows: everyone's, named posts by people you follow, or yours. */
export type ForumTab = "all" | "following" | "mine";

/** The feed's URL state. `category` null means every category; `q` null means no search. */
export interface ForumQuery {
  category: string | null;
  sort: ForumSort;
  q: string | null;
  tab: ForumTab;
}
