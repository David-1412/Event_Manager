import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import type { ReactNode } from "react";
import { EventDetailPage } from "@/components/event/event-detail-view";
import { useEventDetail } from "@/features/events/use-events";
import { capacityLimit } from "@/lib/format";
import type { EventDetail } from "@/types/events";

vi.mock("next/navigation", () => ({
  useParams: () => ({ id: "e-1" }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
  usePathname: () => "/events/e-1",
}));
vi.mock("@/lib/auth/auth-provider", () => ({ useAuth: () => ({ user: null, loading: false }) }));
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
    maxParticipants: null,
    joinedCount: 3,
    interestedCount: 0,
    status: "Scheduled",
    isCancelled: false,
    visibility: "Public",
    distanceKm: null,
    host: { id: "u-h", displayName: "Host", avatarUrl: null },
    isHost: false,
    isJoined: false,
    isInterested: false,
    participants: [],
    cancelledAt: null,
    ...overrides,
  } as EventDetail;
}

const showing = (event: EventDetail) =>
  vi.mocked(useEventDetail).mockReturnValue({ event, error: undefined, isLoading: false, mutate: vi.fn() } as never);

describe("capacityLimit", () => {
  it("treats null, undefined and 0 as no limit", () => {
    expect(capacityLimit(null)).toBeNull();
    expect(capacityLimit(undefined)).toBeNull();
    expect(capacityLimit(0)).toBeNull();
    expect(capacityLimit(12)).toBe(12);
  });
});

describe("event detail capacity", () => {
  it.each([null, undefined, 0])("shows an open event when the capacity is %s", (max) => {
    showing(detail({ maxParticipants: max as number | null }));
    render(<EventDetailPage />);

    expect(screen.getAllByText("Open event").length).toBeGreaterThan(0);
    expect(screen.getByText("Open to all")).toBeInTheDocument();
    expect(document.body.textContent).not.toMatch(/null|undefined|NaN/i);
    expect(document.title).toContain("3 going");
  });

  it("shows spots left and the remaining count when there is a limit", () => {
    showing(detail({ maxParticipants: 10 }));
    render(<EventDetailPage />);

    expect(screen.getAllByText("7 spots left").length).toBeGreaterThan(0);
    expect(screen.getByText("7 of 10")).toBeInTheDocument();
    expect(screen.queryByText("Open event")).toBeNull();
  });
});
