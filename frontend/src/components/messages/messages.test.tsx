import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MessageButton } from "@/components/messages/message-button";
import { CommentsSection } from "@/components/forum/comments-section";
import { messagesHref } from "@/features/messages/messages-api";
import { useComments } from "@/features/messages/use-messages";
import { useAuth } from "@/lib/auth/auth-provider";
import type { ForumPost } from "@/types/forum";
import type { ForumComment } from "@/types/messages";

const push = vi.fn();
vi.mock("next/navigation", () => ({ useRouter: () => ({ push }), usePathname: () => "/events/e-1" }));
vi.mock("@/lib/auth/auth-provider", () => ({ useAuth: vi.fn() }));
vi.mock("@/features/messages/use-messages", () => ({ useComments: vi.fn() }));
vi.mock("@/features/messages/messages-api", async (original) => ({
  ...(await original<object>()),
  startConversation: vi.fn(async () => ({ id: "c-9" })),
}));

const me = { id: "u-me", displayName: "Me", photoURL: null };

describe("MessageButton", () => {
  beforeEach(() => push.mockReset());

  it("renders nothing for your own id or when there is no id (anonymous author)", () => {
    vi.mocked(useAuth).mockReturnValue({ user: me, loading: false } as never);
    const { container, rerender } = render(<MessageButton userId="u-me" />);
    expect(container).toBeEmptyDOMElement();
    rerender(<MessageButton userId={null} />);
    expect(container).toBeEmptyDOMElement();
  });

  it("opens the conversation with the draft", async () => {
    vi.mocked(useAuth).mockReturnValue({ user: me, loading: false } as never);
    render(<MessageButton userId="u-host" draft="Hi! About Netball: " label="Message host" />);
    await userEvent.click(screen.getByRole("button", { name: /Message host/ }));
    expect(push).toHaveBeenCalledWith(messagesHref("c-9", "Hi! About Netball: "));
  });

  it("sends a signed-out visitor to sign in and back", async () => {
    vi.mocked(useAuth).mockReturnValue({ user: null, loading: false } as never);
    render(<MessageButton userId="u-host" />);
    await userEvent.click(screen.getByRole("button", { name: /Message/ }));
    expect(push).toHaveBeenCalledWith("/login?next=%2Fevents%2Fe-1");
  });
});

function comment(overrides: Partial<ForumComment> = {}): ForumComment {
  return {
    id: "k-1",
    parentCommentId: null,
    content: "Nice",
    isDeleted: false,
    isAnonymous: false,
    author: { id: "u-2", name: "Priya Raman", photoUrl: null, role: "Member" },
    isMine: false,
    isOp: false,
    canDelete: false,
    createdAt: new Date().toISOString(),
    replies: [],
    ...overrides,
  };
}

const post = { id: "p-1", isMine: false, isAnonymous: false } as ForumPost;

describe("CommentsSection", () => {
  it("shows anonymous comments without a name and marks the OP", () => {
    vi.mocked(useAuth).mockReturnValue({ user: me, loading: false } as never);
    vi.mocked(useComments).mockReturnValue({
      comments: [comment({ isAnonymous: true, author: null, isOp: true })],
      isLoading: false,
      error: undefined,
      refresh: vi.fn(),
    } as never);
    render(<CommentsSection post={post} />);
    expect(screen.getAllByText("Anonymous").length).toBeGreaterThan(0);
    expect(screen.getByText("OP")).toBeInTheDocument();
    expect(document.body.textContent).not.toMatch(/Priya/);
  });

  it("keeps a deleted comment as a placeholder above its replies", () => {
    vi.mocked(useAuth).mockReturnValue({ user: me, loading: false } as never);
    vi.mocked(useComments).mockReturnValue({
      comments: [comment({ isDeleted: true, content: "", author: null, replies: [comment({ id: "k-2", content: "Still here" })] })],
      isLoading: false,
      error: undefined,
      refresh: vi.fn(),
    } as never);
    render(<CommentsSection post={post} />);
    expect(screen.getByText("[deleted]")).toBeInTheDocument();
    expect(screen.getByText("Still here")).toBeInTheDocument();
  });

  it("offers delete only where the server allows it", () => {
    vi.mocked(useAuth).mockReturnValue({ user: me, loading: false } as never);
    vi.mocked(useComments).mockReturnValue({
      comments: [comment({ id: "a", canDelete: true }), comment({ id: "b", canDelete: false })],
      isLoading: false,
      error: undefined,
      refresh: vi.fn(),
    } as never);
    render(<CommentsSection post={post} />);
    expect(screen.getAllByRole("button", { name: /Delete/ })).toHaveLength(1);
  });

  it("pre-ticks anonymous when an anonymous poster replies in their own thread", () => {
    vi.mocked(useAuth).mockReturnValue({ user: me, loading: false } as never);
    vi.mocked(useComments).mockReturnValue({ comments: [], isLoading: false, error: undefined, refresh: vi.fn() } as never);
    render(<CommentsSection post={{ ...post, isMine: true, isAnonymous: true }} />);
    expect(screen.getByRole("checkbox", { name: "Anonymous" })).toBeChecked();
  });

  it("asks signed-out readers to sign in instead of showing the composer", () => {
    vi.mocked(useAuth).mockReturnValue({ user: null, loading: false } as never);
    vi.mocked(useComments).mockReturnValue({ comments: [], isLoading: false, error: undefined, refresh: vi.fn() } as never);
    render(<CommentsSection post={post} />);
    expect(screen.queryByRole("textbox")).toBeNull();
    expect(screen.getByRole("link", { name: "Sign in" })).toHaveAttribute("href", "/login?next=%2Fforum%2Fp-1");
  });
});
