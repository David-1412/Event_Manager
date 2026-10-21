import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ForumPostView } from "@/components/forum/forum-post-view";
import { deleteForumPost, updateForumPost } from "@/features/forum/forum-api";
import { useForumPost } from "@/features/forum/use-forum";
import type { ForumPost } from "@/types/forum";

const push = vi.fn();
vi.mock("next/navigation", () => ({
  useParams: () => ({ id: "p-1" }),
  useRouter: () => ({ push }),
  usePathname: () => "/forum/p-1",
}));
vi.mock("@/lib/auth/auth-provider", () => ({ useAuth: () => ({ user: { id: "u-me" }, loading: false }) }));
vi.mock("@/components/forum/comments-section", () => ({ CommentsSection: () => null }));
vi.mock("@/features/forum/use-forum", () => ({
  useForumPost: vi.fn(),
  useRecordPostView: vi.fn(),
  useForumPostInterest: () => ({ isInterested: false, likeCount: 0, pending: false, toggle: vi.fn() }),
}));
vi.mock("@/features/forum/forum-api", async (original) => ({
  ...(await original<object>()),
  updateForumPost: vi.fn(async () => ({})),
  deleteForumPost: vi.fn(async () => undefined),
}));

function post(overrides: Partial<ForumPost> = {}): ForumPost {
  return {
    id: "p-1",
    category: { id: 1, name: "General Chat", slug: "general" },
    title: "Lost keys",
    content: "Near the Menzies lift",
    isTruncated: false,
    isAnonymous: false,
    author: { id: "u-me", name: "Me", photoUrl: null, role: "Member" },
    isMine: true,
    isInterested: false,
    likeCount: 0,
    commentCount: 0,
    isPinned: false,
    createdAt: new Date().toISOString(),
    viewCount: 4,
    editedAt: null,
    isDeleted: false,
    canEdit: true,
    canDelete: true,
    ...overrides,
  };
}

const showing = (p: ForumPost) =>
  vi.mocked(useForumPost).mockReturnValue({ post: p, error: undefined, isLoading: false, retry: vi.fn() });

describe("ForumPostView edit and delete", () => {
  beforeEach(() => {
    vi.mocked(updateForumPost).mockClear();
    vi.mocked(deleteForumPost).mockClear();
    push.mockClear();
  });

  it("lets the author edit the title and body in place", async () => {
    showing(post());
    render(<ForumPostView />);

    await userEvent.click(screen.getByRole("button", { name: "Post actions" }));
    await userEvent.click(screen.getByRole("menuitem", { name: "Edit" }));
    const body = screen.getByRole("textbox", { name: "Post" });
    await userEvent.clear(body);
    await userEvent.type(body, "Found them, thanks!");
    await userEvent.click(screen.getByRole("button", { name: "Save" }));

    expect(updateForumPost).toHaveBeenCalledWith("p-1", { title: "Lost keys", content: "Found them, thanks!" });
  });

  it("deletes after confirming and goes back to the forum", async () => {
    vi.spyOn(window, "confirm").mockReturnValue(true);
    showing(post());
    render(<ForumPostView />);

    await userEvent.click(screen.getByRole("button", { name: "Post actions" }));
    await userEvent.click(screen.getByRole("menuitem", { name: "Delete" }));

    expect(deleteForumPost).toHaveBeenCalledWith("p-1");
    expect(push).toHaveBeenCalledWith("/forum");
  });

  it("gives an admin delete but not edit, and a reader no menu at all", async () => {
    showing(post({ isMine: false, canEdit: false, canDelete: true }));
    const { unmount } = render(<ForumPostView />);
    await userEvent.click(screen.getByRole("button", { name: "Post actions" }));
    expect(screen.queryByRole("menuitem", { name: "Edit" })).toBeNull();
    unmount();

    showing(post({ isMine: false, canEdit: false, canDelete: false }));
    render(<ForumPostView />);
    expect(screen.queryByRole("button", { name: "Post actions" })).toBeNull();
  });

  it("marks an edited post", () => {
    showing(post({ editedAt: new Date().toISOString() }));
    render(<ForumPostView />);
    expect(screen.getByText("(edited)")).toBeInTheDocument();
  });

  it("shows a deleted post as a placeholder with no author or actions", () => {
    showing(post({ isDeleted: true, title: null, content: "", author: null, canEdit: false, canDelete: false }));
    render(<ForumPostView />);
    expect(screen.getByText("[This post has been deleted]")).toBeInTheDocument();
    expect(screen.getByText("[deleted]")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Post actions" })).toBeNull();
    expect(document.body.textContent).not.toMatch(/Lost keys|Me\b/);
  });
});
