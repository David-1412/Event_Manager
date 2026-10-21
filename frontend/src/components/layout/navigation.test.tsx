import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { SectionSubnav } from "@/components/layout/section-subnav";
import { SiteHeader } from "@/components/layout/site-header";
import { useNotificationCounts, useUnreadCount } from "@/features/messages/use-messages";
import { useAuth } from "@/lib/auth/auth-provider";

vi.mock("@/lib/auth/auth-provider", () => ({ useAuth: vi.fn() }));
vi.mock("next/navigation", () => ({ useRouter: () => ({ push: vi.fn() }), usePathname: () => "/" }));
vi.mock("next-themes", () => ({ useTheme: () => ({ resolvedTheme: "light", setTheme: vi.fn() }) }));
vi.mock("@/features/messages/use-messages", () => ({
  useUnreadCount: vi.fn(() => 0),
  useNotificationCounts: vi.fn(() => ({ comments: 0, likes: 0, events: 0, social: 0, total: 0 })),
}));

const member = { id: "u-1", uid: "f-1", displayName: "Sam Rivera", email: "sam@student.monash.edu", photoURL: null };

function signedIn(isAdmin: boolean) {
  vi.mocked(useAuth).mockReturnValue({ user: member, loading: false, isAdmin, logout: vi.fn() } as never);
}

/**
 * The navbar's contract after the redesign: two sections in the top bar, and the
 * personal and admin links behind the avatar - with the admin ones only for admins.
 */
describe("SiteHeader", () => {
  beforeEach(() => vi.mocked(useAuth).mockReset());

  it("keeps the top bar to Events, Forum and Messages", () => {
    signedIn(false);
    vi.mocked(useUnreadCount).mockReturnValue(0);
    render(<SiteHeader />);
    const nav = screen.getByRole("navigation", { name: "Primary" });
    expect(within(nav).getAllByRole("link").map((l) => l.textContent)).toEqual(["Events", "Forum", "Messages"]);
    expect(screen.queryByRole("link", { name: "My events" })).toBeNull();
  });

  it("puts My events in the account menu and hides admin links from members", async () => {
    signedIn(false);
    render(<SiteHeader />);
    await userEvent.click(screen.getByRole("button", { name: /Account menu/ }));
    const menu = screen.getByRole("menu");
    expect(within(menu).getByRole("menuitem", { name: /My events/ })).toHaveAttribute("href", "/my-events");
    expect(within(menu).getByRole("menuitem", { name: /Profile/ })).toHaveAttribute("href", "/profile/u-1");
    expect(within(menu).queryByRole("menuitem", { name: /Admin users/ })).toBeNull();
  });

  it("shows the admin links to admins", async () => {
    signedIn(true);
    render(<SiteHeader />);
    await userEvent.click(screen.getByRole("button", { name: /Account menu/ }));
    expect(screen.getByRole("menuitem", { name: /Admin users/ })).toHaveAttribute("href", "/admin/users");
    expect(screen.getByRole("menuitem", { name: /Manage events/ })).toHaveAttribute("href", "/admin/events");
  });

  it("closes the menu on Escape", async () => {
    signedIn(false);
    render(<SiteHeader />);
    await userEvent.click(screen.getByRole("button", { name: /Account menu/ }));
    await userEvent.keyboard("{Escape}");
    expect(screen.queryByRole("menu")).toBeNull();
  });

  it("offers Sign in when signed out", () => {
    vi.mocked(useAuth).mockReturnValue({ user: null, loading: false, isAdmin: false } as never);
    render(<SiteHeader />);
    expect(screen.getByRole("link", { name: "Sign in" })).toHaveAttribute("href", "/login");
  });

  it("shows the unread count on the Messages tab and the chat icon", () => {
    signedIn(false);
    vi.mocked(useUnreadCount).mockReturnValue(3);
    render(<SiteHeader />);
    const nav = screen.getByRole("navigation", { name: "Primary" });
    const tab = within(nav).getByRole("link", { name: "Messages, 3 unread" });
    expect(tab).toHaveAttribute("href", "/messages");
    expect(tab).toHaveTextContent("Messages3");
    // The tab is the only Messages control: no separate chat-bubble icon.
    expect(screen.getAllByRole("link", { name: /Messages/ })).toHaveLength(1);
  });

  it("has no separate notification bell, and counts notifications into the Messages badge", () => {
    signedIn(false);
    vi.mocked(useUnreadCount).mockReturnValue(2);
    vi.mocked(useNotificationCounts).mockReturnValue({ comments: 1, likes: 2, events: 0, social: 0, total: 3 });
    render(<SiteHeader />);
    expect(screen.queryByRole("button", { name: /notification/i })).toBeNull();
    const nav = screen.getByRole("navigation", { name: "Primary" });
    expect(within(nav).getByRole("link", { name: "Messages, 5 unread" })).toBeInTheDocument();
    vi.mocked(useNotificationCounts).mockReturnValue({ comments: 0, likes: 0, events: 0, social: 0, total: 0 });
  });

  it("caps a large unread count", () => {
    signedIn(false);
    vi.mocked(useUnreadCount).mockReturnValue(250);
    render(<SiteHeader />);
    const nav = screen.getByRole("navigation", { name: "Primary" });
    expect(within(nav).getByRole("link", { name: "Messages, 250 unread" })).toHaveTextContent("99+");
  });

  it("keeps the Messages tab for signed-out visitors (the page asks them to sign in)", () => {
    vi.mocked(useUnreadCount).mockReturnValue(0);
    vi.mocked(useAuth).mockReturnValue({ user: null, loading: false, isAdmin: false } as never);
    render(<SiteHeader />);
    expect(screen.getByRole("link", { name: "Messages" })).toHaveAttribute("href", "/messages");
  });
});

describe("SectionSubnav", () => {
  it("marks the current tab and runs in-page switches", async () => {
    const onFollowing = vi.fn();
    render(
      <SectionSubnav
        label="Events"
        items={[
          { label: "All", active: true, onSelect: vi.fn() },
          { label: "Following", active: false, onSelect: onFollowing },
          { label: "My Events", active: false, href: "/my-events" },
        ]}
      />,
    );
    expect(screen.getByRole("button", { name: "All" })).toHaveAttribute("aria-current", "page");
    expect(screen.getByRole("link", { name: "My Events" })).toHaveAttribute("href", "/my-events");
    await userEvent.click(screen.getByRole("button", { name: "Following" }));
    expect(onFollowing).toHaveBeenCalled();
  });

  it("opens Create with both choices, this page's first", async () => {
    render(<SectionSubnav label="Forum" createFirst="post" items={[]} />);
    await userEvent.click(screen.getByRole("button", { name: /Create/ }));
    const choices = screen.getAllByRole("link").map((l) => l.getAttribute("href"));
    expect(choices).toEqual(["/forum/new", "/create"]);
  });
});

describe("ViewModeToggle in the Events sub-nav", () => {
  it("offers List View and Grid View and reports the choice", async () => {
    const { ViewModeToggle } = await import("@/components/event/view-mode-toggle");
    const onMode = vi.fn();
    render(<ViewModeToggle mode="list" onMode={onMode} />);
    expect(screen.getByRole("button", { name: "List View" })).toHaveAttribute("aria-pressed", "true");
    await userEvent.click(screen.getByRole("button", { name: "Grid View" }));
    expect(onMode).toHaveBeenCalledWith("grid");
  });

  it("is shown in the sub-nav row beside Create", () => {
    render(<SectionSubnav label="Events" items={[]} actions={<button type="button">Toggle</button>} />);
    expect(screen.getByRole("button", { name: "Toggle" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Create/ })).toBeInTheDocument();
  });
});
