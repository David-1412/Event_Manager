import { describe, expect, it, vi } from "vitest";

// PostCard now carries the Interested button, which reads the session and can route
// to sign-in. Signed out is the case these tests describe.
vi.mock("@/lib/auth/auth-provider", () => ({ useAuth: () => ({ user: null, loading: false }) }));
vi.mock("next/navigation", () => ({ useRouter: () => ({ push: vi.fn() }), usePathname: () => "/forum" }));
import { render, screen } from "@testing-library/react";
import { PostCard } from "@/components/forum/post-card";
import { isActiveLink } from "@/components/layout/site-header";
import { forumPostsKey } from "@/features/forum/forum-api";
import { parseForumQuery, writeForumQuery } from "@/features/forum/forum-query";
import { emptyTitle } from "@/components/forum/forum-feed-view";
import { searchHref } from "@/components/layout/global-search";
import { formatTimeAgo } from "@/lib/format";
import type { ForumPost } from "@/types/forum";

function post(overrides: Partial<ForumPost> = {}): ForumPost {
  return {
    id: "p-1",
    category: { id: 1, name: "Q&A / Help", slug: "qa" },
    title: "Where is the LTB quiet zone?",
    content: "First week here.",
    isTruncated: false,
    isAnonymous: false,
    author: { id: "u-7", name: "Priya Raman", photoUrl: null, role: "Member" },
    isMine: false,
    isInterested: false,
    likeCount: 0,
    commentCount: 0,
    isPinned: false,
    createdAt: new Date().toISOString(),
    viewCount: 0,
    ...overrides,
  };
}

/**
 * The card renders exactly what the server sent about the author. The server is the
 * anonymity boundary; what matters here is that the card has no fallback that
 * could print a name the payload left out.
 */
describe("PostCard author line", () => {
  it("links a named author to their profile", () => {
    render(<PostCard post={post()} />);
    expect(screen.getByRole("link", { name: /Priya Raman/ }).getAttribute("href")).toBe("/profile/u-7");
  });

  it("shows Anonymous and no profile link when the server withheld the author", () => {
    render(<PostCard post={post({ isAnonymous: true, author: null })} />);
    expect(screen.getAllByText("Anonymous").length).toBeGreaterThan(0);
    expect(screen.queryByRole("link", { name: /Priya/ })).toBeNull();
    expect(document.body.textContent).not.toMatch(/Priya/);
  });

  it("tells the writer their own anonymous post is anonymous to others", () => {
    render(<PostCard post={post({ isAnonymous: true, isMine: true })} />);
    expect(screen.getByText("Posted anonymously")).toBeInTheDocument();
  });

  it("makes the body the link when a post has no title", () => {
    render(<PostCard post={post({ title: null, content: "Anyone else find 8am brutal?" })} />);
    expect(screen.getByRole("link", { name: /8am brutal/ }).getAttribute("href")).toBe("/forum/p-1");
    expect(screen.queryByRole("heading")).toBeNull();
  });

  it("shows Interested with the post's count and its pressed state", () => {
    render(<PostCard post={post({ likeCount: 12, isInterested: true })} />);
    const button = screen.getByRole("button", { name: /Interested/ });
    expect(button).toHaveTextContent("12");
    expect(button).toHaveAttribute("aria-pressed", "true");
  });

  it("marks a truncated excerpt", () => {
    render(<PostCard post={post({ content: "Long body", isTruncated: true })} />);
    expect(screen.getByText(/Long body…/)).toBeInTheDocument();
  });
});

describe("forum URL state", () => {
  it("defaults to hot across every category", () => {
    expect(parseForumQuery(new URLSearchParams(""))).toEqual({ category: null, sort: "hot", q: null, tab: "all" });
  });

  it("treats category=all as no filter and ignores an unknown sort", () => {
    expect(parseForumQuery(new URLSearchParams("category=all&sort=top&tab=nope"))).toEqual({
      category: null,
      sort: "hot",
      q: null,
      tab: "all",
    });
  });

  it("reads the search and the personal tabs", () => {
    expect(parseForumQuery(new URLSearchParams("q=%20ltb%20&tab=mine"))).toMatchObject({ q: "ltb", tab: "mine" });
  });

  it("leaves defaults out of the URL so /forum stays canonical", () => {
    expect(writeForumQuery({ category: null, sort: "hot", q: null, tab: "all" })).toBe("");
    expect(writeForumQuery({ category: "qa", sort: "new", q: "ltb", tab: "following" })).toBe(
      "?category=qa&sort=new&tab=following&q=ltb",
    );
  });

  it("partitions the feed cache by viewer without sending the viewer to the server", () => {
    const key = forumPostsKey({ category: "qa", sort: "new", q: "ltb", tab: "mine" }, "abc", "u-1");
    expect(key).toContain("q=ltb");
    expect(key).toContain("tab=mine");
    expect(key).toContain("category=qa");
    expect(key).toContain("cursor=abc");
    expect(key.endsWith("#viewer=u-1")).toBe(true);
  });
});

describe("formatTimeAgo", () => {
  const now = new Date("2026-10-20T12:00:00Z");
  it.each([
    ["2026-10-20T11:59:30Z", "just now"],
    ["2026-10-20T11:55:00Z", "5m"],
    ["2026-10-20T09:00:00Z", "3h"],
    ["2026-10-18T12:00:00Z", "2d"],
    ["2026-10-20T12:05:00Z", "just now"],
  ])("%s -> %s", (iso, expected) => {
    expect(formatTimeAgo(iso, now)).toBe(expected);
  });

  it("falls back to the date after a week", () => {
    expect(formatTimeAgo("2026-10-01T12:00:00Z", now)).toMatch(/Oct/);
  });
});

describe("isActiveLink", () => {
  it("keeps a section lit on its sub-pages", () => {
    expect(isActiveLink("/forum/p-1", "/forum")).toBe(true);
    expect(isActiveLink("/forum", "/forum")).toBe(true);
  });

  it("lights Messages on the inbox and a conversation", () => {
    expect(isActiveLink("/messages", "/messages")).toBe(true);
    expect(isActiveLink("/messages", "/")).toBe(false);
  });

  it("keeps Events lit on its own pages outside /", () => {
    expect(isActiveLink("/events/e-1", "/")).toBe(true);
    expect(isActiveLink("/my-events", "/")).toBe(true);
    expect(isActiveLink("/create", "/")).toBe(true);
  });

  it("never lights Events on another section", () => {
    expect(isActiveLink("/forum", "/")).toBe(false);
    expect(isActiveLink("/", "/")).toBe(true);
  });

  it("does not match a section that merely shares a prefix", () => {
    expect(isActiveLink("/forumx", "/forum")).toBe(false);
  });
});

/**
 * Where each search chip sends a query: Events and Posts search inside their own
 * page (keeping that page's filters when you are already on it), All opens the
 * combined results page.
 */
describe("searchHref", () => {
  const none = new URLSearchParams();

  it("sends All to the combined results page", () => {
    expect(searchHref("all", " ltb ", none, "events")).toBe("/search?q=ltb");
  });

  it("filters the Events page in place, keeping its filters", () => {
    expect(searchHref("events", "netball", new URLSearchParams("date=week&q=old"), "events")).toBe(
      "/?date=week&q=netball",
    );
  });

  it("starts a fresh Forum search when coming from Events", () => {
    expect(searchHref("posts", "ltb", new URLSearchParams("date=week"), "events")).toBe("/forum?q=ltb");
  });

  it("keeps the forum category and sort when searching within the Forum", () => {
    expect(searchHref("posts", "ltb", new URLSearchParams("category=qa&sort=new"), "forum")).toBe(
      "/forum?category=qa&sort=new&q=ltb",
    );
  });

  it("clearing the box removes the search but not the filters", () => {
    expect(searchHref("events", "  ", new URLSearchParams("date=week&q=old"), "events")).toBe("/?date=week");
    expect(searchHref("all", "", none, "forum")).toBe("/forum");
  });
});

describe("forum empty states", () => {
  const base = { category: null, sort: "hot" as const, q: null, tab: "all" as const };

  it("names the search, not an empty forum", () => {
    expect(emptyTitle({ ...base, q: "ltb" })).toBe('No posts match "ltb".');
    expect(emptyTitle({ ...base, q: "ltb" }, "Q&A / Help")).toBe('No posts in Q&A / Help match "ltb".');
  });

  it("speaks to the personal tabs", () => {
    expect(emptyTitle({ ...base, tab: "following" })).toBe("No posts from people you follow yet.");
    expect(emptyTitle({ ...base, tab: "mine" })).toBe("You haven't posted yet.");
  });
});
