import type { ForumQuery, ForumSort, ForumTab } from "@/types/forum";

/**
 * The forum feed's URL state: `/forum?category=qa&sort=new`. The URL is the only
 * place it lives, as on Browse, so Back, reload and a shared link all land on the
 * same feed.
 *
 * Defaults are left out of the URL (`sort=hot`, no category), so the plain `/forum`
 * link and a reset both produce the canonical address.
 */
const SORTS: readonly ForumSort[] = ["hot", "new"];
const TABS: readonly ForumTab[] = ["all", "following", "mine"];

export function parseForumQuery(params: URLSearchParams): ForumQuery {
  const category = params.get("category")?.trim() || null;
  const sort = params.get("sort");
  const tab = params.get("tab");
  return {
    category: category === "all" ? null : category,
    sort: SORTS.includes(sort as ForumSort) ? (sort as ForumSort) : "hot",
    q: params.get("q")?.trim() || null,
    tab: TABS.includes(tab as ForumTab) ? (tab as ForumTab) : "all",
  };
}

export function writeForumQuery(query: ForumQuery): string {
  const params = new URLSearchParams();
  if (query.category) params.set("category", query.category);
  if (query.sort !== "hot") params.set("sort", query.sort);
  if (query.tab !== "all") params.set("tab", query.tab);
  if (query.q) params.set("q", query.q);
  const search = params.toString();
  return search ? `?${search}` : "";
}
