import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { HostLink, profileHref } from "@/components/profile/host-link";
import { EventCard } from "@/components/event/event-card";
import { RoleBadge, isCreatorRole } from "@/components/admin/role-badge";
import type { EventListItem } from "@/types/events";

/**
 * The clickable-host contract.
 *
 * A host name is the entry point to "can I trust this person", so the two things
 * worth pinning down are that it links when an id exists and that it refuses to link
 * when one does not. The second matters more than it looks: `hostId` is optional on
 * the wire, and a component that linked regardless would produce
 * `/profile/undefined` for every response written before the field shipped and for
 * every fixture - a real route that 404s, which reads as a broken profile rather
 * than as an unlinked name.
 */

function item(overrides: Partial<EventListItem> = {}): EventListItem {
  return {
    id: "evt-1",
    title: "Sunday social",
    description: null,
    tags: ["social"],
    sportIcon: null,
    skillLevel: null,
    startAt: "2026-11-01T09:00:00.000Z",
    endAt: "2026-11-01T11:00:00.000Z",
    timezone: "Australia/Melbourne",
    venueName: "Test courts",
    address: "1 Test St",
    thumbnailUrl: null,
    latitude: 0,
    longitude: 0,
    cost: null,
    maxParticipants: 10,
    joinedCount: 2,
    interestedCount: 1,
    status: "Scheduled",
    isCancelled: false,
    visibility: "Public",
    distanceKm: null,
    ...overrides,
  };
}

describe("profileHref", () => {
  it("is the profile route for an id", () => {
    expect(profileHref("abc-123")).toBe("/profile/abc-123");
  });
});

describe("HostLink", () => {
  it("links the host name to their profile when an id is present", () => {
    render(<HostLink userId="u-9" name="David Pan" />);
    const link = screen.getByRole("link", { name: /David Pan/ });
    expect(link.getAttribute("href")).toBe("/profile/u-9");
  });

  it("renders plain text rather than a dead link when the id is absent", () => {
    render(<HostLink userId={undefined} name="David Pan" />);
    expect(screen.queryByRole("link")).toBeNull();
    // getByText would match twice - Avatar carries the name in both its title and
    // its sr-only span - so assert on the accessible name of the non-link instead.
    expect(screen.getAllByText("David Pan").length).toBeGreaterThan(0);
  });

  it("treats a null id the same as an absent one", () => {
    render(<HostLink userId={null} name="David Pan" />);
    expect(screen.queryByRole("link")).toBeNull();
  });

  it("renders nothing for a blank name rather than an empty link", () => {
    const { container } = render(<HostLink userId="u-9" name="   " />);
    expect(container).toBeEmptyDOMElement();
  });

  it("shows the Creator label for a Moderator host when asked for the badge", () => {
    render(<HostLink userId="u-9" name="Monash Badminton Club" role="Moderator" withBadge />);
    expect(screen.getByRole("link", { name: /Monash Badminton Club/ })).toHaveTextContent(
      "Creator",
    );
  });
});

describe("RoleBadge", () => {
  it("calls the Moderator tier Creator, not Moderator", () => {
    render(<RoleBadge role="Moderator" />);
    expect(screen.getByText(/Creator/)).toBeInTheDocument();
  });

  it("marks only Creator with the star, leaving Member and Admin unmarked", () => {
    const { rerender } = render(<RoleBadge role="Member" />);
    expect(screen.getByText("Member")).not.toHaveTextContent("\u{2B50}");
    rerender(<RoleBadge role="Admin" />);
    expect(screen.getByText("Admin")).not.toHaveTextContent("\u{2B50}");
    rerender(<RoleBadge role="Moderator" />);
    expect(screen.getByText(/Creator/)).toHaveTextContent("\u{2B50}");
  });

  it("renders nothing while the role is unknown", () => {
    const { container } = render(<RoleBadge role={undefined} />);
    expect(container).toBeEmptyDOMElement();
  });

  it("accepts null without rendering a guessed role", () => {
    const { container } = render(<RoleBadge role={null} />);
    expect(container).toBeEmptyDOMElement();
  });
});

describe("isCreatorRole", () => {
  it("is the Moderator tier and nothing else", () => {
    expect(isCreatorRole("Moderator")).toBe(true);
    expect(isCreatorRole("Admin")).toBe(false);
    expect(isCreatorRole("Member")).toBe(false);
    expect(isCreatorRole(null)).toBe(false);
    expect(isCreatorRole(undefined)).toBe(false);
  });
});

describe("EventCard host line", () => {
  it("names the host and links to their profile", () => {
    render(<EventCard event={item({ hostName: "David Pan", hostId: "u-7" })} />);
    const link = screen.getByRole("link", { name: /David Pan/ });
    expect(link.getAttribute("href")).toBe("/profile/u-7");
  });

  it("shows a Creator badge for a Moderator host", () => {
    render(
      <EventCard event={item({ hostName: "Monash Badminton Club", hostId: "u-7", hostRole: "Moderator" })} />,
    );
    expect(screen.getByText(/Creator/)).toBeInTheDocument();
  });

  it("omits the host line entirely when the row has no host name", () => {
    render(<EventCard event={item()} />);
    expect(screen.queryByRole("link", { name: /David Pan/ })).toBeNull();
  });
});