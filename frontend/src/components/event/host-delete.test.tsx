import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ReactNode } from "react";
import { EventDetailPage } from "@/components/event/event-detail-view";
import { HostingActions } from "@/components/event/hosting-actions";
import { deleteCancelledEvent, setEventStatus } from "@/features/events/host-actions";
import { useEventDetail } from "@/features/events/use-events";
import type { EventDetail } from "@/types/events";

const push = vi.fn();
vi.mock("next/navigation", () => ({
  useParams: () => ({ id: "e-1" }),
  useRouter: () => ({ push, replace: vi.fn() }),
  usePathname: () => "/events/e-1",
}));
vi.mock("@/lib/auth/auth-provider", () => ({ useAuth: () => ({ user: { id: "u-h" }, loading: false }) }));
vi.mock("@/features/events/use-events", () => ({
  useEventDetail: vi.fn(),
  useJoinEvent: () => ({ join: vi.fn(), leave: vi.fn(), pending: false, failure: null }),
}));
vi.mock("@/features/events/use-interests", () => ({
  useToggleInterest: () => ({ toggle: vi.fn(), pending: false }),
}));
vi.mock("@/features/events/host-actions", async (original) => ({
  ...(await original<object>()),
  setEventStatus: vi.fn(async () => undefined),
  deleteCancelledEvent: vi.fn(async () => undefined),
  refreshEventCaches: vi.fn(async () => undefined),
}));
vi.mock("@/components/map/event-map", () => ({ EventMap: () => null }));
vi.mock("@/components/event/join-requests-panel", () => ({ JoinRequestsPanel: () => null }));
vi.mock("@/components/layout/site-header", () => ({
  StickyFooter: ({ children }: { children: ReactNode }) => <div>{children}</div>,
}));

function detail(overrides: Partial<EventDetail> = {}): EventDetail {
  return {
    id: "e-1",
    title: "Social run",
    description: null,
    tags: [],
    sportIcon: null,
    skillLevel: null,
    startAt: "2030-06-04T08:00:00.000Z",
    endAt: "2030-06-04T10:00:00.000Z",
    timezone: "Australia/Melbourne",
    venueName: "Clayton oval",
    address: "Wellington Rd",
    thumbnailUrl: null,
    latitude: -37.91,
    longitude: 145.13,
    cost: null,
    maxParticipants: 10,
    joinedCount: 3,
    interestedCount: 0,
    status: "Scheduled",
    isCancelled: false,
    visibility: "Public",
    distanceKm: null,
    host: { id: "u-h", displayName: "Host", avatarUrl: null },
    isHost: true,
    isJoined: false,
    isInterested: false,
    participants: [],
    cancelledAt: null,
    ...overrides,
  } as EventDetail;
}

const showing = (event: EventDetail) =>
  vi.mocked(useEventDetail).mockReturnValue({ event, error: undefined, isLoading: false, mutate: vi.fn() } as never);

beforeEach(() => {
  vi.mocked(setEventStatus).mockClear();
  vi.mocked(deleteCancelledEvent).mockClear();
  push.mockClear();
});

describe("event page host menu", () => {
  it("offers Edit and Cancel on a live event, and cancelling calls the API", async () => {
    showing(detail());
    render(<EventDetailPage />);

    await userEvent.click(screen.getByRole("button", { name: "Host actions" }));
    expect(screen.getByRole("menuitem", { name: "Edit" })).toBeInTheDocument();
    expect(screen.queryByRole("menuitem", { name: "Delete event" })).toBeNull();
    await userEvent.click(screen.getByRole("menuitem", { name: "Cancel event" }));
    await userEvent.click(screen.getByRole("button", { name: "Cancel event" }));

    expect(setEventStatus).toHaveBeenCalledWith("e-1", "cancel");
  });

  it("offers Reopen and Delete once cancelled, and deleting goes back to My Events", async () => {
    showing(detail({ status: "Cancelled", isCancelled: true }));
    render(<EventDetailPage />);

    await userEvent.click(screen.getByRole("button", { name: "Host actions" }));
    expect(screen.getByRole("menuitem", { name: "Reopen event" })).toBeInTheDocument();
    await userEvent.click(screen.getByRole("menuitem", { name: "Delete event" }));
    await userEvent.click(screen.getByRole("button", { name: "Delete event" }));

    expect(deleteCancelledEvent).toHaveBeenCalledWith("e-1");
    expect(push).toHaveBeenCalledWith("/my-events");
  });
});

describe("My Events hosting actions", () => {
  it("shows only Cancel for a live event", () => {
    render(<HostingActions eventId="e-1" isCancelled={false} isEnded={false} />);
    expect(screen.getByRole("button", { name: "Cancel event" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Delete" })).toBeNull();
  });

  it("deletes a cancelled event after confirming", async () => {
    vi.spyOn(window, "confirm").mockReturnValue(true);
    render(<HostingActions eventId="e-1" isCancelled isEnded={false} />);
    expect(screen.getByRole("button", { name: "Reopen" })).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Delete" }));
    expect(deleteCancelledEvent).toHaveBeenCalledWith("e-1");
  });

  it("still lets a host clear out a cancelled event that has passed", () => {
    render(<HostingActions eventId="e-1" isCancelled isEnded />);
    expect(screen.getByRole("button", { name: "Delete" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Reopen" })).toBeNull();
  });
});
