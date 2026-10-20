import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { SectionSubnav } from "@/components/layout/section-subnav";
import { SiteHeader } from "@/components/layout/site-header";
import { useAuth } from "@/lib/auth/auth-provider";

vi.mock("@/lib/auth/auth-provider", () => ({ useAuth: vi.fn() }));
vi.mock("next/navigation", () => ({ useRouter: () => ({ push: vi.fn() }), usePathname: () => "/" }));
vi.mock("next-themes", () => ({ useTheme: () => ({ resolvedTheme: "light", setTheme: vi.fn() }) }));
vi.mock("@/components/layout/notification-bell", () => ({ NotificationBell: () => null }));

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

  it("keeps the top bar to Events and Forum", () => {
    signedIn(false);
    render(<SiteHeader />);
    const nav = screen.getByRole("navigation", { name: "Primary" });
    expect(within(nav).getAllByRole("link").map((l) => l.textContent)).toEqual(["Events", "Forum"]);
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

  it("shows messages as coming soon rather than a live button", () => {
    signedIn(false);
    render(<SiteHeader />);
    expect(screen.getByRole("button", { name: /Messages/ })).toBeDisabled();
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
