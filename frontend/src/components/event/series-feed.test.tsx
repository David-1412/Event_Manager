import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ReactNode } from "react";
import { EventCard } from "@/components/event/event-card";
import { EventDetailPage } from "@/components/event/event-detail-view";
import { useEventDetail } from "@/features/events/use-events";
import { useEventSessions } from "@/features/events/use-sessions";
import type { EventDetail, EventListItem, EventSession } from "@/types/events";

const push = vi.fn();
vi.mock("next/navigation", () => ({
  useParams: () => ({ id: "s-1" }),
  useRouter: () => ({ push, replace: vi.fn() }),
  usePathname: () => "/events/s-1",
}));
vi.mock("@/lib/auth/auth-provider", () => ({ useAuth: () => ({ user: { id: "u-me" }, loading: false }) }));
vi.mock("@/features/events/use-sessions", async (original) => ({
  ...(await original<object>()),
  useEventSessions: vi.fn(),
}));
vi.mock("@/features/events/use-events", () => ({
  useEventDetail: vi.fn(),
  useJoinEvent: () => ({ join: vi.fn(), leave: vi.fn(), pending: false, failure: null }),
}));
vi.mock("@/features/events/use-interests", () => ({
  useToggleInterest: () => ({ toggle: vi.fn(), pending: false }),
}));
vi.mock("@/components/map/event-map", () => ({ EventMap: () => null }));
vi.mock("@/components/event/join-requests-panel", () => ({ JoinRequestsPanel: () => null }));
vi.mock("@/components/layout/site-header", () => ({
  StickyFooter: ({ children }: { children: ReactNode }) => <div>{children}</div>,
}));

afterEach(cleanup);

// Fri 23 Oct 2026, 6pm Melbourne (UTC+11), then weekly.
const sessionAt = (week: number, overrides: Partial<EventSession> = {}): EventSession => ({
  id: `s-${week + 1}`,
  startAt: new Date(Date.UTC(2026, 9, 23, 7) + week * 7 * 86_400_000).toISOString(),
  endAt: new Date(Date.UTC(2026, 9, 23, 9) + week * 7 * 86_400_000).toISOString(),
  joinedCount: 2,
  maxParticipants: 10,
  isCancelled: false,
  isJoined: false,
  ...overrides,
});
const SESSIONS = [sessionAt(0), sessionAt(1, { isJoined: true }), sessionAt(2, { joinedCount: 10 })];

const listItem = (overrides: Partial<EventListItem> = {}): EventListItem => ({
  id: "s-1",
  title: "Friday social hit",
  tags: [],
  sportIcon: null,
  skillLevel: null,
  startAt: SESSIONS[0].startAt,
  endAt: SESSIONS[0].endAt,
  timezone: "Australia/Melbourne",
  venueName: "Sports centre",
  address: "",
  thumbnailUrl: null,
  latitude: 0,
  longitude: 0,
  cost: null,
  maxParticipants: 10,
  joinedCount: 2,
  interestedCount: 0,
  status: "Scheduled",
  isCancelled: false,
  visibility: "Public",
  distanceKm: null,
  isRecurring: true,
  seriesId: "s-1",
  recurrenceFrequency: "WEEKLY",
  recurrenceSummary: "Weekly on Fri · 12 sessions",
  seriesUpcomingCount: 12,
  ...overrides,
});

describe("series card on browse", () => {
  it("shows the repeat badge and the next-session pill", () => {
    render(<EventCard event={listItem()} />);
    expect(screen.getByText("Repeats weekly")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Next: Fri 23 Oct · View all 12 sessions/ })).toBeInTheDocument();
    expect(useEventSessions).not.toHaveBeenCalled(); // nothing fetched until opened
  });

  it("opens a drawer of the upcoming dates, each linking to its session", async () => {
    vi.mocked(useEventSessions).mockReturnValue({ sessions: SESSIONS, error: undefined, isLoading: false });
    render(<EventCard event={listItem()} />);

    const pill = screen.getByRole("button", { name: /View all 12 sessions/ });
    await userEvent.click(pill);

    expect(pill).toHaveAttribute("aria-expanded", "true");
    const links = within(document.getElementById("sessions-s-1")!).getAllByRole("link");
    expect(links.map((a) => a.getAttribute("href"))).toEqual(["/events/s-1", "/events/s-2", "/events/s-3"]);
    expect(screen.getByText("Going")).toBeInTheDocument();
    expect(screen.getByText("Full")).toBeInTheDocument();
  });

  it("does not open the card when the pill is clicked", async () => {
    vi.mocked(useEventSessions).mockReturnValue({ sessions: SESSIONS, error: undefined, isLoading: false });
    const onSelect = vi.fn();
    render(<EventCard event={listItem()} onSelect={onSelect} />);
    await userEvent.click(screen.getByRole("button", { name: /View all 12 sessions/ }));
    expect(onSelect).not.toHaveBeenCalled();
  });

  it("shows no pill for a one-off event or a series with one session left", () => {
    const { rerender } = render(
      <EventCard event={listItem({ isRecurring: false, seriesId: null, seriesUpcomingCount: null, recurrenceFrequency: null })} />,
    );
    expect(screen.queryByRole("button", { name: /View all/ })).toBeNull();
    expect(screen.queryByText(/Repeats/)).toBeNull();

    rerender(<EventCard event={listItem({ seriesUpcomingCount: 1 })} />);
    expect(screen.queryByRole("button", { name: /View all/ })).toBeNull();
    expect(screen.getByText("Repeats weekly")).toBeInTheDocument();
  });
});

describe("session picker on the detail page", () => {
  it("lists the series' dates and opens the one picked", async () => {
    vi.mocked(useEventSessions).mockReturnValue({ sessions: SESSIONS, error: undefined, isLoading: false });
    vi.mocked(useEventDetail).mockReturnValue({
      event: {
        ...listItem(),
        host: { id: "u-h", displayName: "Host", avatarUrl: null },
        isHost: false,
        isJoined: false,
        isInterested: false,
        participants: [],
        cancelledAt: null,
      } as EventDetail,
      error: undefined,
      isLoading: false,
      mutate: vi.fn(),
    } as never);
    render(<EventDetailPage />);

    const picker = screen.getByRole("combobox", { name: "Select session date" });
    expect(picker).toHaveValue("s-1");
    expect(screen.getByRole("option", { name: /\(going\)/ })).toHaveValue("s-2");
    expect(screen.getByRole("option", { name: /\(full\)/ })).toHaveValue("s-3");

    await userEvent.selectOptions(picker, "s-3");
    expect(push).toHaveBeenCalledWith("/events/s-3");
  });
});
