import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { NotificationPanel, NotificationTabs } from "@/components/messages/notification-center";
import { filterConversations } from "@/components/messages/messages-view";
import { markActivityRead } from "@/features/messages/messages-api";
import { useActivity, useEventNotifications, useNotificationCounts } from "@/features/messages/use-messages";
import { markAllNotificationsRead } from "@/lib/notifications";
import type { ActivityItem, Conversation } from "@/types/messages";

const push = vi.fn();
vi.mock("next/navigation", () => ({ useRouter: () => ({ push }) }));
vi.mock("@/features/messages/use-messages", () => ({
  useActivity: vi.fn(),
  useEventNotifications: vi.fn(),
  useNotificationCounts: vi.fn(),
}));
vi.mock("@/features/messages/messages-api", async (original) => ({
  ...(await original<object>()),
  markActivityRead: vi.fn(async () => undefined),
}));
vi.mock("@/lib/notifications", () => ({
  markNotificationRead: vi.fn(async () => undefined),
  markAllNotificationsRead: vi.fn(async () => undefined),
}));

function item(overrides: Partial<ActivityItem> = {}): ActivityItem {
  return {
    key: "like:1",
    kind: "Likes",
    actor: { id: "u-2", name: "Priya Raman", photoUrl: null, role: "Member" },
    action: "is interested in your post",
    targetTitle: "Where is the LTB?",
    snippet: null,
    href: "/forum/p-1",
    createdAt: new Date().toISOString(),
    isRead: false,
    ...overrides,
  };
}

describe("NotificationTabs", () => {
  beforeEach(() => {
    vi.mocked(useNotificationCounts).mockReturnValue({ comments: 5, likes: 12, events: 0, social: 3, total: 20 });
  });

  it("shows the four tabs in order, each with its unread count", () => {
    render(<NotificationTabs active={null} onSelect={vi.fn()} />);
    const tabs = within(screen.getByRole("navigation", { name: "Notifications" })).getAllByRole("button");
    expect(tabs.map((t) => t.getAttribute("aria-label"))).toEqual([
      "Comments, 5 unread",
      "Likes, 12 unread",
      "Events",
      "Social, 3 unread",
    ]);
  });

  it("marks the open tab and reports a choice", async () => {
    const onSelect = vi.fn();
    render(<NotificationTabs active="likes" onSelect={onSelect} />);
    expect(screen.getByRole("button", { name: /Likes/ })).toHaveAttribute("aria-current", "page");
    await userEvent.click(screen.getByRole("button", { name: /Social/ }));
    expect(onSelect).toHaveBeenCalledWith("social");
  });
});

describe("NotificationPanel", () => {
  beforeEach(() => {
    push.mockReset();
    vi.mocked(markActivityRead).mockClear();
  });

  it("opening a tab marks its unread items read, and keeps them highlighted as New", async () => {
    const mutate = vi.fn(async () => undefined);
    vi.mocked(useActivity).mockReturnValue({
      items: [item(), item({ key: "like:2", isRead: true, actor: { id: "u-3", name: "Sam", photoUrl: null, role: "Member" } })],
      error: undefined,
      isLoading: false,
      mutate,
    } as never);
    render(<NotificationPanel tab="likes" onBack={vi.fn()} />);

    // Read on open - no click needed - and only the ones that were unread.
    await waitFor(() => expect(markActivityRead).toHaveBeenCalledWith(["like:1"]));
    expect(markActivityRead).toHaveBeenCalledTimes(1);

    // The avatar carries the name too, so it appears more than once.
    expect(screen.getAllByText("Priya Raman").length).toBeGreaterThan(0);
    expect(screen.getAllByText("New")).toHaveLength(1);

    await userEvent.click(screen.getByRole("button", { name: /Priya Raman/ }));
    expect(push).toHaveBeenCalledWith("/forum/p-1");
    expect(markActivityRead).toHaveBeenCalledTimes(1);
  });

  it("names an anonymous commenter as Anonymous", () => {
    vi.mocked(useActivity).mockReturnValue({
      items: [item({ kind: "Comments", actor: null, action: "replied to your comment" })],
      error: undefined,
      isLoading: false,
      mutate: vi.fn(),
    } as never);
    render(<NotificationPanel tab="comments" onBack={vi.fn()} />);
    expect(screen.getAllByText("Anonymous").length).toBeGreaterThan(0);
    expect(document.body.textContent).not.toMatch(/Priya/);
  });

  it("reads every unread item in the tab in one call, without a button", async () => {
    const mutate = vi.fn(async () => undefined);
    vi.mocked(useActivity).mockReturnValue({
      items: [item({ key: "a" }), item({ key: "b" }), item({ key: "c", isRead: true })],
      error: undefined,
      isLoading: false,
      mutate,
    } as never);
    render(<NotificationPanel tab="likes" onBack={vi.fn()} />);
    await waitFor(() => expect(markActivityRead).toHaveBeenCalledWith(["a", "b"]));
    expect(screen.queryByRole("button", { name: "Mark all as read" })).toBeNull();
  });

  it("shows the event notifications that used to be in the bell", async () => {
    vi.mocked(useEventNotifications).mockReturnValue({
      list: {
        items: [
          { id: "n-1", title: "New Event", message: "David posted Netball", type: "x", link: "/events/e-1", read: false, createdAt: new Date().toISOString() },
        ],
        unreadCount: 1,
      },
      error: undefined,
      isLoading: false,
      mutate: vi.fn(async () => undefined),
    } as never);
    render(<NotificationPanel tab="events" onBack={vi.fn()} />);
    await waitFor(() => expect(markAllNotificationsRead).toHaveBeenCalledTimes(1));
    await userEvent.click(screen.getByRole("button", { name: /New Event/ }));
    expect(push).toHaveBeenCalledWith("/events/e-1");
  });

  it("makes no read call when nothing in the tab is unread", async () => {
    vi.mocked(useActivity).mockReturnValue({
      items: [item({ isRead: true })],
      error: undefined,
      isLoading: false,
      mutate: vi.fn(),
    } as never);
    render(<NotificationPanel tab="likes" onBack={vi.fn()} />);
    await new Promise((r) => setTimeout(r, 0));
    expect(markActivityRead).not.toHaveBeenCalled();
    expect(screen.queryByText("New")).toBeNull();
  });

  it("explains an empty tab", () => {
    vi.mocked(useActivity).mockReturnValue({ items: [], error: undefined, isLoading: false, mutate: vi.fn() } as never);
    render(<NotificationPanel tab="social" onBack={vi.fn()} />);
    expect(screen.getByText("No new followers yet.")).toBeInTheDocument();
  });
});

describe("filterConversations", () => {
  const convo = (name: string) => ({ id: name, other: { id: name, name, photoUrl: null, role: "Member" } }) as Conversation;
  const all = [convo("Sam Rivera"), convo("John Smith"), convo("Alice Chen")];

  it("matches part of the other person's name, ignoring case", () => {
    expect(filterConversations(all, "ri").map((c) => c.id)).toEqual(["Sam Rivera"]);
    expect(filterConversations(all, "SMITH").map((c) => c.id)).toEqual(["John Smith"]);
  });

  it("shows everyone for an empty search", () => {
    expect(filterConversations(all, "  ")).toHaveLength(3);
  });
});
